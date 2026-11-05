using Radial.Core.Model;
using Radial.Windows.Shortcuts;

namespace Radial.Windows.Presentation;

/// <summary>Una tecla ofrecida en la lista manual: su nombre estable, cómo se llama en el teclado activo y su grupo.</summary>
public sealed record KeyOption(string Name, string Label, string GroupName);

/// <summary>
/// El selector de atajos del formulario. Hay dos maneras de elegir la combinación, que dan siempre el mismo resultado (nombre
/// estable de tecla + modificadores): GRABARLA (solo mientras el usuario ha activado el modo de grabación; nunca hay captura
/// permanente) o elegirla a mano (modificadores y lista de teclas, para lo que Windows se queda, como Win+E). Una combinación no
/// utilizable nunca se acepta: <see cref="Chord"/> es <c>null</c> y <see cref="Message"/> explica por qué.
/// No usa WPF: la vista le pasa los códigos de tecla virtual ya traducidos.
/// </summary>
public sealed class KeyCaptureViewModel : ObservableObject
{
    private readonly IKeyLabelProvider _labels;
    private bool _isRecording;
    private KeyModifiers _modifiers;
    private string? _keyName;
    private string _message = string.Empty;
    private KeyModifiers _pending;

    public KeyCaptureViewModel(IKeyLabelProvider labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _labels = labels;
        Options = KeyVocabulary.All
            .Select(k => new KeyOption(k.Name, KeyChordText.KeyText(k.Name, _labels.GetLabel), GroupTitle(k.Group)))
            .ToList();
    }

    /// <summary>Cambió la combinación (por grabación o a mano).</summary>
    public event Action? ChordChanged;

    /// <summary>Todas las teclas del vocabulario con su nombre en el teclado activo, por grupos, para la lista manual.</summary>
    public IReadOnlyList<KeyOption> Options { get; }

    public string LayoutName => _labels.LayoutName;

    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (Set(ref _isRecording, value))
            {
                Raise(nameof(Display));
            }
        }
    }

    public KeyModifiers Modifiers
    {
        get => _modifiers;
        private set
        {
            if (Set(ref _modifiers, value))
            {
                Raise(nameof(Control));
                Raise(nameof(Shift));
                Raise(nameof(Alt));
                Raise(nameof(Windows));
            }
        }
    }

    public string? KeyName
    {
        get => _keyName;
        set
        {
            if (Set(ref _keyName, value))
            {
                Changed();
            }
        }
    }

    public bool Control { get => _modifiers.HasFlag(KeyModifiers.Control); set => SetModifier(KeyModifiers.Control, value); }

    public bool Shift { get => _modifiers.HasFlag(KeyModifiers.Shift); set => SetModifier(KeyModifiers.Shift, value); }

    public bool Alt { get => _modifiers.HasFlag(KeyModifiers.Alt); set => SetModifier(KeyModifiers.Alt, value); }

    public bool Windows { get => _modifiers.HasFlag(KeyModifiers.Windows); set => SetModifier(KeyModifiers.Windows, value); }

    /// <summary>Por qué no se acepta lo grabado o elegido; vacío si todo está bien (o si todavía no hay tecla).</summary>
    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    /// <summary>La combinación completa y válida, o <c>null</c> (sin tecla, o con una combinación que no se puede usar).</summary>
    public KeyChord? Chord
    {
        get
        {
            if (string.IsNullOrEmpty(_keyName))
            {
                return null;
            }

            var chord = new KeyChord(_modifiers, _keyName);
            return new SendKeysAction(chord).GetProblems().Any() ? null : chord;
        }
    }

    /// <summary>Lo que se muestra en el campo: la combinación en el teclado activo, lo grabado hasta ahora o una indicación.</summary>
    public string Display
    {
        get
        {
            if (_isRecording)
            {
                var held = _pending != KeyModifiers.None ? KeyChordText.Format(new KeyChord(_pending, string.Empty)).TrimEnd(' ', '+') + " + …" : "Pulsa la combinación…";
                return held;
            }

            return string.IsNullOrEmpty(_keyName) ? "Sin combinación" : KeyChordText.Format(new KeyChord(_modifiers, _keyName), _labels.GetLabel);
        }
    }

    /// <summary>Carga una combinación existente (al editar una acción) sin producir cambios pendientes.</summary>
    public void Load(KeyChord? chord)
    {
        _isRecording = false;
        _pending = KeyModifiers.None;
        _modifiers = chord?.Modifiers ?? KeyModifiers.None;
        _keyName = chord?.Key;
        _message = string.Empty;
        RaiseAll();
    }

    /// <summary>Activa el modo de grabación: a partir de ahora la vista le pasa las pulsaciones.</summary>
    public void StartRecording()
    {
        _pending = KeyModifiers.None;
        Message = string.Empty;
        IsRecording = true;
        Raise(nameof(Display));
    }

    /// <summary>Sale del modo de grabación sin tocar la combinación.</summary>
    public void StopRecording()
    {
        if (!_isRecording)
        {
            return;
        }

        _pending = KeyModifiers.None;
        IsRecording = false;
        Raise(nameof(Display));
    }

    public void Clear()
    {
        _isRecording = false;
        _pending = KeyModifiers.None;
        _modifiers = KeyModifiers.None;
        _keyName = null;
        Message = string.Empty;
        RaiseAll();
        ChordChanged?.Invoke();
    }

    /// <summary>Se mantienen estos modificadores (para mostrar «Ctrl + …» mientras se espera la tecla).</summary>
    public void SetHeldModifiers(KeyModifiers held)
    {
        if (!_isRecording || held == _pending)
        {
            return;
        }

        _pending = held;
        Raise(nameof(Display));
    }

    /// <summary>
    /// Una pulsación durante la grabación. <paramref name="virtualKey"/> es el código <c>VK_*</c> de la tecla. Devuelve
    /// <c>true</c> si la pulsación se ha consumido (la vista no debe dejar que llegue a ningún otro control).
    /// </summary>
    public bool HandleKey(int virtualKey, KeyModifiers held, bool? extended = null)
    {
        if (!_isRecording)
        {
            return false;
        }

        _pending = held;
        switch (KeyCapture.Evaluate(virtualKey, held, extended))
        {
            case KeyCaptureWaiting:
                Raise(nameof(Display));
                break;

            case KeyCaptured captured:
                _modifiers = captured.Chord.Modifiers;
                _keyName = captured.Chord.Key;
                _pending = KeyModifiers.None;
                _message = string.Empty;
                _isRecording = false;
                RaiseAll();
                ChordChanged?.Invoke();
                break;

            case KeyCaptureRejected rejected:
                Message = rejected.Reason;
                Raise(nameof(Display));
                break;
        }

        return true;
    }

    private void SetModifier(KeyModifiers flag, bool on)
    {
        var next = on ? _modifiers | flag : _modifiers & ~flag;
        if (next == _modifiers)
        {
            return;
        }

        Modifiers = next;
        Changed();
    }

    /// <summary>Tras una elección manual: se recalcula el aviso y se notifica.</summary>
    private void Changed()
    {
        if (!string.IsNullOrEmpty(_keyName))
        {
            var problem = new SendKeysAction(new KeyChord(_modifiers, _keyName)).GetProblems().FirstOrDefault();
            Message = problem ?? string.Empty;
        }
        else
        {
            Message = string.Empty;
        }

        Raise(nameof(Display));
        Raise(nameof(Chord));
        ChordChanged?.Invoke();
    }

    private void RaiseAll()
    {
        Raise(nameof(IsRecording));
        Raise(nameof(Modifiers));
        Raise(nameof(Control));
        Raise(nameof(Shift));
        Raise(nameof(Alt));
        Raise(nameof(Windows));
        Raise(nameof(KeyName));
        Raise(nameof(Message));
        Raise(nameof(Display));
        Raise(nameof(Chord));
    }

    private static string GroupTitle(KeyGroup group) => group switch
    {
        KeyGroup.Letters => "Letras",
        KeyGroup.Digits => "Números",
        KeyGroup.Function => "Funciones",
        KeyGroup.Editing => "Edición",
        KeyGroup.Navigation => "Navegación",
        KeyGroup.Numpad => "Teclado numérico",
        KeyGroup.Punctuation => "Signos",
        KeyGroup.System => "Sistema",
        KeyGroup.Media => "Multimedia",
        KeyGroup.Browser => "Navegador",
        _ => group.ToString(),
    };
}

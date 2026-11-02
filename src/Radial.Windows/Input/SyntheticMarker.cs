using System.Security.Cryptography;

namespace Radial.Windows.Input;

/// <summary>
/// Marca (<c>dwExtraInfo</c>) que Radial pone en los eventos que inyecta durante la autoprueba. Windows distingue "inyectado"
/// de "físico" pero no QUIÉN inyectó: cualquier otra aplicación (macros, remapeadores, escritorio remoto...) produce
/// eventos con el mismo flag. La marca permite aceptar solo los propios.
///
/// Es de 32 bits a propósito. Medido en esta máquina: el hook de teclado entrega <c>dwExtraInfo</c> completo (64 bits), pero
/// el de ratón lo entrega TRUNCADO a los 32 bits bajos (se envió 0x8D9904E27A4D5E79 y llegó 0x7A4D5E79). Una marca de 64 bits
/// nunca habría coincidido en el ratón. Por eso la comparación usa solo los 32 bits bajos, también en el teclado.
///
/// El valor es aleatorio y distinto en cada proceso, así que otra aplicación no puede conocerlo de antemano. No es un
/// mecanismo de seguridad contra alguien en la misma sesión (puede leer la memoria del proceso): evita confundir por accidente
/// entradas ajenas con las de la autoprueba. Nunca es cero: cero es lo que llevan los eventos sin marca.
/// </summary>
internal static class SyntheticMarker
{
    private const long LowBits = 0xFFFFFFFFL;

    /// <summary>Marca de este proceso (1 a 2^32 - 1). Es lo que se envía en <c>dwExtraInfo</c>.</summary>
    public static long Value { get; } = Create();

    /// <summary>Verdadero si <paramref name="extraInfo"/> lleva la marca, mirando solo los 32 bits bajos.</summary>
    public static bool Matches(long extraInfo) => (extraInfo & LowBits) == Value;

    private static long Create()
    {
        long value;
        do
        {
            value = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue) & LowBits;
        }
        while (value == 0);

        return value;
    }
}

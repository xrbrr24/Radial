using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Tests.Support;
using Radial.Core.Validation;

namespace Radial.Core.Tests.Model;

/// <summary>
/// La validación de <see cref="SendKeysAction"/> usa el vocabulario cerrado de <see cref="KeyVocabulary"/> para la tecla y
/// sigue validando los modificadores con las reglas de siempre.
/// </summary>
public class SendKeysActionValidationTests
{
    public static TheoryData<string> ApprovedNames => KeyVocabularyTests.ApprovedNames;

    public static TheoryData<string> RejectedNames => KeyVocabularyTests.RejectedNames;

    private static SendKeysAction Action(string key, KeyModifiers modifiers = KeyModifiers.None) => new(new KeyChord(modifiers, key));

    private static ValidationResult Validate(RadialAction action) =>
        ConfigurationValidator.Validate(TestConfigs.WithMenu(
            TestConfigs.Standard(),
            TestConfigs.Menu(TestConfigs.Work, TestConfigs.Item("x", RadialDirection.North, action))));

    [Theory]
    [MemberData(nameof(ApprovedNames))]
    public void EveryApprovedKey_PassesValidation_InAnyCase(string name)
    {
        foreach (var spelling in new[] { name, name.ToLowerInvariant(), name.ToUpperInvariant() })
        {
            var action = Action(spelling);

            Assert.Empty(action.GetProblems());
            Assert.True(Validate(action).IsValid, $"'{spelling}' debería pasar la validación de la configuración.");
        }
    }

    [Theory]
    [MemberData(nameof(RejectedNames))]
    public void EveryKeyOutsideTheVocabulary_IsReported(string name)
    {
        var action = Action(name);

        Assert.Single(action.GetProblems());
        Assert.Contains(ValidationCodes.ActionInvalid, Validate(action).Errors.Select(e => e.Code));
    }

    [Theory]
    [InlineData("Ñ")]
    [InlineData("Semicolon")]
    [InlineData("CapsLock")]
    [InlineData("NoExisteEstaTecla")]
    public void TheKeysThatUsedToPass_ButAreNotApproved_AreNowRejected(string name)
    {
        // Antes de unificar el vocabulario, cualquier nombre de hasta 32 caracteres sin espacios validaba.
        Assert.False(Validate(Action(name, KeyModifiers.Control)).IsValid);
    }

    [Fact]
    public void ANullKey_IsReportedWithoutThrowing()
    {
        Assert.NotEmpty(new SendKeysAction(new KeyChord(KeyModifiers.Control, null!)).GetProblems());
    }

    [Fact]
    public void ANullChord_IsStillReported()
    {
        Assert.NotEmpty(new SendKeysAction(null!).GetProblems());
    }

    [Fact]
    public void AHugeKey_IsRejected_WithoutEchoingItInTheMessage()
    {
        var problems = Action(new string('A', 100_000)).GetProblems().ToList();

        Assert.Single(problems);
        // El mensaje lista el vocabulario (largo) pero no repite el texto recibido, que mide 100 000 caracteres.
        Assert.True(problems[0].Length < KeyVocabulary.Description.Length + 300, "El mensaje no debe repetir el texto recibido.");
    }

    [Fact]
    public void TheMessage_ListsTheApprovedKeys()
    {
        var problem = Assert.Single(Action("Ñ").GetProblems());

        Assert.Contains(KeyVocabulary.Description, problem);
    }

    // Los modificadores se siguen validando como antes

    [Fact]
    public void EveryCombinationOfKnownModifiers_IsAccepted_WithAnApprovedKey()
    {
        for (var bits = 0; bits <= 15; bits++)
        {
            Assert.Empty(Action("T", (KeyModifiers)bits).GetProblems());
        }
    }

    [Fact]
    public void UnknownModifierBits_AreStillReported_WithAnApprovedKey()
    {
        Assert.Single(Action("T", (KeyModifiers)64).GetProblems());
        Assert.Single(Action("T", KeyModifiers.Control | (KeyModifiers)16).GetProblems());
    }

    [Fact]
    public void AnInvalidKey_AndInvalidModifiers_AreBothReported()
    {
        Assert.Equal(2, Action("Ñ", (KeyModifiers)64).GetProblems().Count());
    }

    [Fact]
    public void AValidKey_WithNoModifiers_IsAccepted()
    {
        Assert.Empty(Action("F5").GetProblems());
    }
}

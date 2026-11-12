using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using Radial.Windows.Runtime;

namespace Radial.Windows.Tests.Runtime;

/// <summary>
/// La compilación en ejecución tiene que poder verse: una instancia antigua de Radial en la bandeja hace que abrir otra compilación solo
/// la traiga al frente (instancia única), y lo que el usuario ve parece un fallo del código nuevo.
/// </summary>
public class BuildIdentityTests
{
    private static readonly DateTime Moment = new(2026, 11, 11, 19, 17, 42, DateTimeKind.Local);

    [Fact]
    public void TheStamp_HasMinutes_ForInvariantCulture()
    {
        Assert.Equal("2026-11-11 19:17", BuildIdentity.FormatStamp(Moment));
    }

    [Fact]
    public void TheStamp_DoesNotDependOnTheCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");

            Assert.Equal("2026-11-11 19:17", BuildIdentity.FormatStamp(Moment));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void TwoBuildsAMinuteApart_LookDifferent()
    {
        Assert.NotEqual(BuildIdentity.FormatStamp(Moment), BuildIdentity.FormatStamp(Moment.AddMinutes(1)));
    }

    [Fact]
    public void TheWindowTitleAndTheTrayText_CarryTheStamp()
    {
        var stamp = BuildIdentity.FormatStamp(Moment);

        Assert.Contains(stamp, BuildIdentity.WindowTitle(stamp));
        Assert.Contains(stamp, BuildIdentity.TrayText(stamp));
    }

    [Theory]
    [InlineData("2026-11-11 19:17")]
    [InlineData("desconocida")]
    [InlineData("un texto larguísimo que no debería salir de ningún formateo pero que no puede romper la bandeja de Windows")]
    public void TheTrayText_NeverExceedsTheLimitOfNotifyIcon(string stamp)
    {
        Assert.True(BuildIdentity.TrayText(stamp).Length <= BuildIdentity.MaxTrayText);
    }

    [Fact]
    public void TheLogLine_SaysWhichBuildAndFromWhere_OrJustWhichBuild()
    {
        Assert.Contains(@"C:\Apps\Radial", BuildIdentity.Describe("2026-11-11 19:17", @"C:\Apps\Radial"));
        Assert.Contains("2026-11-11 19:17", BuildIdentity.Describe("2026-11-11 19:17", @"C:\Apps\Radial"));
        Assert.DoesNotContain("ejecutándose desde", BuildIdentity.Describe("2026-11-11 19:17", null));
        Assert.DoesNotContain("ejecutándose desde", BuildIdentity.Describe("2026-11-11 19:17", "  "));
    }

    [Fact]
    public void ARealAssembly_GivesAStampInTheExpectedFormat_AndItsFolder()
    {
        var assembly = typeof(BuildIdentity).Assembly;

        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$"), BuildIdentity.StampOf(assembly));
        Assert.Equal(Path.GetDirectoryName(assembly.Location), BuildIdentity.DirectoryOf(assembly));
    }

    [Fact]
    public void AnAssemblyWithoutAFileOnDisk_IsUnknown_AndNeverThrows()
    {
        var dynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RadialDynamicForTests"), AssemblyBuilderAccess.Run);

        Assert.Equal(BuildIdentity.Unknown, BuildIdentity.StampOf(dynamicAssembly));
        Assert.Null(BuildIdentity.DirectoryOf(dynamicAssembly));
    }
}

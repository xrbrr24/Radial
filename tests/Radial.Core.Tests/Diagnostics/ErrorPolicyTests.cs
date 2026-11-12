using System.Runtime.InteropServices;
using Radial.Core.Diagnostics;
using Radial.Core.Tests.Support;

namespace Radial.Core.Tests.Diagnostics;

/// <summary>La política de recuperación: qué errores permiten seguir, cuáles comprometen una función y cuáles obligan a terminar.</summary>
public class ErrorClassifierTests
{
    [Theory]
    [InlineData(ErrorOrigin.UiThread, ErrorImpact.Recoverable)]
    [InlineData(ErrorOrigin.UnobservedTask, ErrorImpact.Recoverable)]
    [InlineData(ErrorOrigin.Action, ErrorImpact.Recoverable)]
    [InlineData(ErrorOrigin.Shutdown, ErrorImpact.Recoverable)]
    [InlineData(ErrorOrigin.Notification, ErrorImpact.Recoverable)]
    [InlineData(ErrorOrigin.Storage, ErrorImpact.Degraded)]
    [InlineData(ErrorOrigin.Capture, ErrorImpact.Degraded)]
    [InlineData(ErrorOrigin.Startup, ErrorImpact.Fatal)]
    [InlineData(ErrorOrigin.BackgroundThread, ErrorImpact.Fatal)]
    public void AnOrdinaryException_IsClassifiedByWhereItHappened(ErrorOrigin origin, ErrorImpact expected)
    {
        Assert.Equal(expected, ErrorClassifier.Classify(new InvalidOperationException("normal"), origin));
    }

    public static TheoryData<Exception> FatalExceptions() => new()
    {
        new OutOfMemoryException(),
        new AccessViolationException(),
        new StackOverflowException(),
        new InvalidProgramException(),
        new BadImageFormatException(),
        new SEHException(),
        new AppDomainUnloadedException(),
        new CannotUnloadAppDomainException(),
        new TypeInitializationException("Tipo", new InvalidOperationException()),
    };

    [Theory]
    [MemberData(nameof(FatalExceptions))]
    public void AFatalType_IsFatalWhereverItHappens(Exception exception)
    {
        Assert.True(ErrorClassifier.IsFatalType(exception));
        Assert.Equal(ErrorImpact.Fatal, ErrorClassifier.Classify(exception, ErrorOrigin.UiThread));
        Assert.Equal(ErrorImpact.Fatal, ErrorClassifier.Classify(exception, ErrorOrigin.Action));
        Assert.Equal(ErrorImpact.Fatal, ErrorClassifier.Classify(exception, ErrorOrigin.UnobservedTask));
    }

    [Fact]
    public void AFatalException_InsideAnotherOne_MakesTheWholeThingFatal()
    {
        var wrapped = new InvalidOperationException("envuelta", new AccessViolationException());
        var aggregate = new AggregateException(new TimeoutException(), new OutOfMemoryException());

        Assert.Equal(ErrorImpact.Fatal, ErrorClassifier.Classify(wrapped, ErrorOrigin.UiThread));
        Assert.Equal(ErrorImpact.Fatal, ErrorClassifier.Classify(aggregate, ErrorOrigin.UnobservedTask));
    }

    [Fact]
    public void OrdinaryNestedExceptions_AreNotFatal()
    {
        var nested = new InvalidOperationException("a", new IOException("b", new TimeoutException("c")));

        Assert.False(ErrorClassifier.IsFatalType(nested));
        Assert.Equal(ErrorImpact.Recoverable, ErrorClassifier.Classify(nested, ErrorOrigin.UiThread));
    }

    [Fact]
    public void AFailureOfTheNotificationItself_NeverEscalates_NotEvenForAFatalType()
    {
        // Si un icono de bandeja roto pudiera tumbar la aplicación, el aviso sería peor que el fallo que avisa.
        Assert.Equal(ErrorImpact.Recoverable, ErrorClassifier.Classify(new OutOfMemoryException(), ErrorOrigin.Notification));
    }

    [Fact]
    public void AVeryDeepChain_IsAnalysedWithoutRecursionOrHanging()
    {
        Exception chain = new AccessViolationException();
        for (var level = 0; level < 1000; level++)
        {
            chain = new InvalidOperationException("nivel", chain);
        }

        // La profundidad analizada está acotada: lo que hay más allá del límite no se mira (y nunca se desborda la pila).
        var result = ErrorClassifier.IsFatalType(chain);

        Assert.False(result);
    }

    [Fact]
    public void ClassifyNeedsAnException()
    {
        Assert.Throws<ArgumentNullException>(() => ErrorClassifier.Classify(null!, ErrorOrigin.UiThread));
    }
}

/// <summary>El presupuesto de errores recuperables de la interfaz: más de los permitidos en la ventana agota la paciencia.</summary>
public class ErrorBudgetTests
{
    [Fact]
    public void UpToTheLimit_TheBudgetHolds_AndTheNextErrorExhaustsIt()
    {
        var budget = new ErrorBudget(new ManualClock(), maxErrors: 5);

        for (var count = 1; count <= 5; count++)
        {
            Assert.False(budget.Register());
        }

        Assert.True(budget.Register());
        Assert.Equal(6, budget.Count);
    }

    [Fact]
    public void ErrorsThatAreFarApart_NeverAddUp()
    {
        var clock = new ManualClock();
        var budget = new ErrorBudget(clock, maxErrors: 3, window: TimeSpan.FromSeconds(60));

        for (var count = 0; count < 50; count++)
        {
            Assert.False(budget.Register());
            clock.Advance(TimeSpan.FromSeconds(30)); // como mucho dos dentro de cualquier ventana de 60 s
        }

        Assert.True(budget.Count <= 2);
    }

    [Fact]
    public void WhenTheWindowPasses_TheBudgetIsRestored()
    {
        var clock = new ManualClock();
        var budget = new ErrorBudget(clock, maxErrors: 2, window: TimeSpan.FromSeconds(10));
        Assert.False(budget.Register());
        Assert.False(budget.Register());
        Assert.True(budget.Register());

        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(0, budget.Count);
        Assert.False(budget.Register());
    }

    [Fact]
    public void TheWindowSlides_OnlyTheOldestErrorsLeave()
    {
        var clock = new ManualClock();
        var budget = new ErrorBudget(clock, maxErrors: 3, window: TimeSpan.FromSeconds(10));
        budget.Register(); // t=0
        clock.Advance(TimeSpan.FromSeconds(6));
        budget.Register(); // t=6
        budget.Register(); // t=6
        clock.Advance(TimeSpan.FromSeconds(5)); // t=11: el de t=0 ya salió

        Assert.Equal(2, budget.Count);
        Assert.False(budget.Register()); // 3 dentro de la ventana: aún no se supera
        Assert.True(budget.Register()); // 4
    }

    [Fact]
    public void InvalidArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ErrorBudget(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ErrorBudget(new ManualClock(), maxErrors: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ErrorBudget(new ManualClock(), window: TimeSpan.Zero));
    }

    [Fact]
    public async Task ItIsSafeFromSeveralThreads()
    {
        var budget = new ErrorBudget(new ManualClock(), maxErrors: 1000);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var count = 0; count < 100; count++)
            {
                budget.Register();
            }
        })));

        Assert.Equal(800, budget.Count);
    }
}

/// <summary>Las tormentas de avisos: un aviso igual no se repite antes de su ventana y hay un máximo global por minuto.</summary>
public class NotificationThrottleTests
{
    private static NotificationThrottle Throttle(ManualClock clock, int maxPerWindow = 3) =>
        new(clock, perKeyWindow: TimeSpan.FromSeconds(30), maxPerWindow: maxPerWindow, globalWindow: TimeSpan.FromSeconds(60));

    [Fact]
    public void TheFirstNotice_IsShown_WithNothingSuppressed()
    {
        var decision = Throttle(new ManualClock()).Evaluate("clave");

        Assert.True(decision.Show);
        Assert.Equal(0, decision.Suppressed);
    }

    [Fact]
    public void ARepeatedNotice_IsSuppressedAndCounted_ThenShownWithTheCount()
    {
        var clock = new ManualClock();
        var throttle = Throttle(clock);
        Assert.True(throttle.Evaluate("clave").Show);

        Assert.False(throttle.Evaluate("clave").Show);
        clock.Advance(TimeSpan.FromSeconds(10));
        var second = throttle.Evaluate("clave");
        clock.Advance(TimeSpan.FromSeconds(10));
        var third = throttle.Evaluate("clave");

        Assert.False(second.Show);
        Assert.Equal(2, second.Suppressed);
        Assert.False(third.Show);
        Assert.Equal(3, third.Suppressed);

        clock.Advance(TimeSpan.FromSeconds(11)); // 31 s desde el último que se mostró
        var shown = throttle.Evaluate("clave");

        Assert.True(shown.Show);
        Assert.Equal(3, shown.Suppressed);
        Assert.Equal(1, throttle.Evaluate("clave").Suppressed); // el contador se reinició: el siguiente silenciado cuenta 1
    }

    [Fact]
    public void DifferentNotices_DoNotSilenceEachOther()
    {
        var throttle = Throttle(new ManualClock());

        Assert.True(throttle.Evaluate("a").Show);
        Assert.True(throttle.Evaluate("b").Show);
        Assert.False(throttle.Evaluate("a").Show);
    }

    [Fact]
    public void ManyDifferentNotices_AreCappedPerWindow_AndRecoverAfterwards()
    {
        var clock = new ManualClock();
        var throttle = Throttle(clock, maxPerWindow: 3);
        Assert.True(throttle.Evaluate("a").Show);
        Assert.True(throttle.Evaluate("b").Show);
        Assert.True(throttle.Evaluate("c").Show);

        var overflow = throttle.Evaluate("d");

        Assert.False(overflow.Show);
        Assert.Equal(1, overflow.Suppressed);

        clock.Advance(TimeSpan.FromSeconds(61));
        var later = throttle.Evaluate("d");

        Assert.True(later.Show);
        Assert.Equal(1, later.Suppressed); // se dice cuántos avisos iguales se callaron
    }

    [Fact]
    public void MemoryIsBounded_EvenWithAThousandDifferentKeys()
    {
        var clock = new ManualClock();
        var throttle = Throttle(clock, maxPerWindow: 5);

        for (var key = 0; key < 1000; key++)
        {
            throttle.Evaluate("clave-" + key);
            clock.Advance(TimeSpan.FromSeconds(40));
        }

        Assert.True(throttle.Evaluate("nueva").Show);
    }

    [Fact]
    public void InvalidArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new NotificationThrottle(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NotificationThrottle(new ManualClock(), maxPerWindow: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NotificationThrottle(new ManualClock(), perKeyWindow: TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => Throttle(new ManualClock()).Evaluate(null!));
    }
}

/// <summary>Los nombres de los archivos de registro y qué se borra para cumplir la retención.</summary>
public class LogRetentionTests
{
    private static LogFileInfo LogFile(int day, int sequence, long length = 100) =>
        new(LogRetention.FileName(new DateOnly(2026, 11, day), sequence), length);

    [Fact]
    public void FileNames_SortChronologically_AsText()
    {
        var names = new[]
        {
            LogRetention.FileName(new DateOnly(2026, 11, 10), 2),
            LogRetention.FileName(new DateOnly(2026, 11, 9), 12),
            LogRetention.FileName(new DateOnly(2026, 11, 10), 1),
            LogRetention.FileName(new DateOnly(2026, 12, 1), 1),
        };

        var sorted = names.OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal("radial-20261109-012.log", sorted[0]);
        Assert.Equal("radial-20261110-001.log", sorted[1]);
        Assert.Equal("radial-20261110-002.log", sorted[2]);
        Assert.Equal("radial-20261201-001.log", sorted[3]);
    }

    [Theory]
    [InlineData("radial-20261110-001.log", true)]
    [InlineData("radial-20261110-1.log", false)]
    [InlineData("radial-2026-11-10.log", false)]
    [InlineData("config.json", false)]
    [InlineData("last-fatal.txt", false)]
    [InlineData("radial-20261110-001.log.bak", false)]
    [InlineData("", false)]
    public void OnlyTheFilesOfTheLogFollowTheNamePattern(string name, bool expected)
    {
        Assert.Equal(expected, LogRetention.IsLogFile(name));
    }

    [Fact]
    public void ASequenceBelowOne_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LogRetention.FileName(new DateOnly(2026, 11, 10), 0));
    }

    [Fact]
    public void WithinTheLimits_NothingIsDeleted()
    {
        var files = new[] { LogFile(8, 1), LogFile(9, 1), LogFile(10, 1) };

        Assert.Empty(LogRetention.SelectToDelete(files, maxFiles: 3, maxTotalBytes: 10_000));
    }

    [Fact]
    public void TooManyFiles_DeletesTheOldestFirst()
    {
        var files = new[] { LogFile(10, 1), LogFile(8, 1), LogFile(9, 1), LogFile(7, 1) };

        var delete = LogRetention.SelectToDelete(files, maxFiles: 2, maxTotalBytes: 10_000);

        Assert.Equal(new[] { LogRetention.FileName(new DateOnly(2026, 11, 7), 1), LogRetention.FileName(new DateOnly(2026, 11, 8), 1) }, delete);
    }

    [Fact]
    public void TooManyBytes_DeletesTheOldestUntilItFits_ButNeverTheNewest()
    {
        var files = new[] { LogFile(8, 1, 600), LogFile(9, 1, 600), LogFile(10, 1, 600) };

        var delete = LogRetention.SelectToDelete(files, maxFiles: 10, maxTotalBytes: 700);

        Assert.Equal(2, delete.Count);
        Assert.DoesNotContain(LogRetention.FileName(new DateOnly(2026, 11, 10), 1), delete);
    }

    [Fact]
    public void ASingleFileBiggerThanTheTotal_IsKept()
    {
        var delete = LogRetention.SelectToDelete([LogFile(10, 1, 5_000)], maxFiles: 1, maxTotalBytes: 100);

        Assert.Empty(delete);
    }

    [Fact]
    public void FilesThatAreNotLogs_AreNeverSelected()
    {
        var files = new[] { new LogFileInfo("last-fatal.txt", 9_999_999), new LogFileInfo("otro.log", 9_999_999), LogFile(10, 1) };

        Assert.Empty(LogRetention.SelectToDelete(files, maxFiles: 1, maxTotalBytes: 1_000));
    }

    [Fact]
    public void InvalidLimits_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LogRetention.SelectToDelete([], maxFiles: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogRetention.SelectToDelete([], maxTotalBytes: 0));
        Assert.Throws<ArgumentNullException>(() => LogRetention.SelectToDelete(null!));
    }
}

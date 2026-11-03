using System.Reflection;
using Radial.Core.Execution;
using Radial.Core.Model;

namespace Radial.Core.Tests.Execution;

/// <summary>
/// Complementa <see cref="ArchitectureTests"/> para el contrato de ejecución: ni la API ni el ensamblado de Radial.Core
/// pueden depender de lo que sirve para ejecutar cosas en el sistema (procesos, red) ni exponer tipos de fuera del núcleo.
/// </summary>
public class ExecutionContractIndependenceTests
{
    private const string ExecutionNamespace = "Radial.Core.Execution";

    private static readonly Assembly Core = typeof(IActionExecutor).Assembly;

    [Fact]
    public void Core_DoesNotReferenceProcessOrNetworkAssemblies()
    {
        // Lanzar procesos, abrir URLs o enviar teclas son infraestructura: viven en Radial.Windows, nunca en Core.
        var forbidden = new[] { "System.Diagnostics.Process", "System.Net", "Microsoft.Win32", "System.Windows" };

        var referenced = Core.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

        Assert.DoesNotContain(referenced, name => forbidden.Any(f => name.StartsWith(f, StringComparison.Ordinal)));
    }

    [Fact]
    public void TheExecutionContract_OnlyExposesTypesFromCoreOrTheBaseLibrary()
    {
        var types = Core.GetTypes().Where(t => t.Namespace == ExecutionNamespace).ToList();
        Assert.NotEmpty(types); // evita una prueba vacua si el espacio de nombres cambia

        var foreign = types
            .SelectMany(UsedTypes)
            .SelectMany(Flatten)
            .Where(t => t.Assembly != Core && t.Assembly != typeof(object).Assembly)
            .Select(t => t.FullName)
            .Distinct()
            .ToList();

        Assert.Empty(foreign);
    }

    [Fact]
    public void TheExecutionContract_IntegratesWithTheExistingActionHierarchy()
    {
        var execute = typeof(IActionExecutor).GetMethod(nameof(IActionExecutor.ExecuteAsync));
        var canExecute = typeof(IActionExecutor).GetMethod(nameof(IActionExecutor.CanExecute));

        Assert.NotNull(execute);
        Assert.NotNull(canExecute);
        Assert.Equal(typeof(RadialAction), execute.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(RadialAction), canExecute.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), execute.GetParameters()[1].ParameterType);
        Assert.Equal(typeof(Task<ActionExecutionResult>), execute.ReturnType);
    }

    // Tipos que aparecen en las firmas, campos y propiedades de un tipo (también los no públicos).
    private static IEnumerable<Type> UsedTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(all))
        {
            yield return field.FieldType;
        }

        foreach (var property in type.GetProperties(all))
        {
            yield return property.PropertyType;
        }

        foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
        {
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }

            if (method is MethodInfo info)
            {
                yield return info.ReturnType;
            }
        }
    }

    // Task<ActionExecutionResult>[] -> Task<>, ActionExecutionResult...
    private static IEnumerable<Type> Flatten(Type type)
    {
        if (type.HasElementType)
        {
            foreach (var inner in Flatten(type.GetElementType()!))
            {
                yield return inner;
            }

            yield break;
        }

        yield return type;
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments().SelectMany(Flatten))
            {
                yield return argument;
            }
        }
    }
}

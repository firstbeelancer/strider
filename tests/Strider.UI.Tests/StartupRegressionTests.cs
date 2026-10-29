using System.Reflection;
using Avalonia.Controls;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Strider.UI;

namespace Strider.UI.Tests;

/// <summary>
/// Guards against the two bugs that made v0.1.0-rc1..rc3 unusable:
/// MainWindowViewModel could not be resolved from DI (IImapGateway was never
/// registered), and no View called InitializeComponent, so windows were empty.
/// </summary>
public class StartupRegressionTests
{
    private static readonly Assembly Ui = typeof(App).Assembly;

    public static IEnumerable<object[]> ViewModelTypes() =>
        Ui.GetTypes()
            .Where(t => t.Namespace == "Strider.UI.ViewModels" && t.Name.EndsWith("ViewModel") && !t.IsAbstract)
            .Select(t => new object[] { t.Name });

    public static IEnumerable<object[]> ViewTypes() =>
        Ui.GetTypes()
            .Where(t => t.Namespace == "Strider.UI.Views" && typeof(Control).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => new object[] { t.Name });

    [Theory]
    [MemberData(nameof(ViewModelTypes))]
    public void EveryViewModel_ResolvesFromDi(string name)
    {
        // ConfigureServices touches the user's data dir; point it at a temp folder.
        Environment.SetEnvironmentVariable("STRIDER_DATA_DIR", Path.Combine(Path.GetTempPath(), "strider-tests"));
        var sp = App.ConfigureServices();
        var type = Ui.GetType($"Strider.UI.ViewModels.{name}")!;

        var act = () => sp.GetRequiredService(type);

        act.Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(ViewTypes))]
    public void EveryView_ConstructorCallsInitializeComponent(string name)
    {
        var type = Ui.GetType($"Strider.UI.Views.{name}")!;
        var ctor = type.GetConstructor(Type.EmptyTypes)!;
        var init = type.GetMethod("InitializeComponent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

        // call/callvirt opcode followed by the metadata token of InitializeComponent.
        var il = ctor.GetMethodBody()!.GetILAsByteArray()!;
        var token = BitConverter.GetBytes(init.MetadataToken);
        var found = Enumerable.Range(0, il.Length - 4)
            .Any(i => (il[i] == 0x28 || il[i] == 0x6F) && il.AsSpan(i + 1, 4).SequenceEqual(token));

        found.Should().BeTrue($"{name}() must call InitializeComponent(), otherwise the XAML is never loaded");
    }
}

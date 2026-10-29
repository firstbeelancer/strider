using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Strider.UI;
using Strider.UI.ViewModels;
using Strider.UI.Views;

[assembly: AvaloniaTestApplication(typeof(Strider.UI.Tests.HeadlessApp))]

namespace Strider.UI.Tests;

public static class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        Environment.SetEnvironmentVariable("STRIDER_DATA_DIR",
            Path.Combine(Path.GetTempPath(), "strider-headless-" + Guid.NewGuid().ToString("N")));
        return AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}

/// <summary>
/// Real UI smoke on a headless Avalonia platform: windows render content and the
/// account wizard is reachable. Screenshots go to $TMP/strider-shots for review.
/// </summary>
public class HeadlessUiTests
{
    public static readonly string ShotDir = Path.Combine(Path.GetTempPath(), "strider-shots");
    static HeadlessUiTests() => Directory.CreateDirectory(ShotDir);

    private static IServiceProvider Services => ((App)Application.Current!).Services;

    private static void Shot(Window w, string name)
    {
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame()?.Save(Path.Combine(ShotDir, name + ".png"));
    }

    private static Button Button(Window w, string text) =>
        w.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == text);

    [AvaloniaFact]
    public async Task FirstRun_WithoutAccounts_ShowsWelcome_AndOpensWizard()
    {
        var vm = Services.GetRequiredService<MainWindowViewModel>();
        var main = new MainWindow { DataContext = vm };
        main.Show();

        await vm.LoadAccountsCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Shot(main, "main-first-run");

        vm.HasNoAccounts.Should().BeTrue();
        vm.IsAccountWizardOpen.Should().BeTrue("first run must open the account wizard by itself");
    }

    [AvaloniaFact]
    public void Wizard_EmailStep_HasFields_AndStepText()
    {
        var w = new AccountWizardWindow { DataContext = Services.GetRequiredService<AccountWizardViewModel>() };
        w.Show();
        Shot(w, "wizard-step1");

        w.GetVisualDescendants().OfType<TextBox>().Count(t => t.IsEffectivelyVisible).Should().BeGreaterThanOrEqualTo(3);
        w.GetVisualDescendants().OfType<TextBlock>().Should().Contain(t => t.Text == "Step 1 of 2");
    }

    [AvaloniaFact]
    public void Wizard_AutoDetect_Gmail_ShowsServerStep()
    {
        var vm = Services.GetRequiredService<AccountWizardViewModel>();
        var w = new AccountWizardWindow { DataContext = vm };
        w.Show();
        vm.Email = "someone@gmail.com";
        vm.Password = "x";

        vm.AutoDetectSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Shot(w, "wizard-step2");

        vm.IsServerStep.Should().BeTrue();
        vm.ImapHost.Should().Be("imap.gmail.com");
        vm.SmtpHost.Should().Be("smtp.gmail.com");
    }

    [AvaloniaFact]
    public void Settings_And_Compose_Render()
    {
        var s = new SettingsWindow { DataContext = Services.GetRequiredService<SettingsViewModel>() };
        s.Show();
        Shot(s, "settings");
        var c = new ComposeWindow { DataContext = Services.GetRequiredService<ComposeViewModel>() };
        c.Show();
        Shot(c, "compose");
        c.GetVisualDescendants().OfType<Button>().Should().Contain(b => b.Content as string == "Send");
    }
}

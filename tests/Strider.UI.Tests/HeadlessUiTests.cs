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
        var frame = w.CaptureRenderedFrame();
        if (frame is null) return;
        using var file = File.Create(Path.Combine(ShotDir, name + ".png"));
        frame.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
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
    public async Task Settings_AiTab_IsBoundToTheViewModel()
    {
        var vm = Services.GetRequiredService<SettingsViewModel>();
        var s = new SettingsWindow { DataContext = vm };
        s.Show();
        await vm.LoadCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        // TabControl builds only the selected tab, so switch to the AI tab first.
        var tabs = s.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedIndex = tabs.ItemCount - 1 - 1; // "AI" (last tab is PGP)
        Dispatcher.UIThread.RunJobs();
        Shot(s, "settings-ai");

        // The AI tab must expose provider, model and key fields wired to the VM.
        s.GetVisualDescendants().OfType<ComboBox>().Should().NotBeEmpty();
        s.GetVisualDescendants().OfType<TextBox>().Should().Contain(tb => tb.PasswordChar == '*');
        vm.AiProviders.Should().Contain("openai");
    }

    [AvaloniaFact]
    public void Calendar_Window_RendersMonthGrid()
    {
        var vm = Services.GetRequiredService<CalendarViewModel>();
        var w = new CalendarWindow { DataContext = vm };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Shot(w, "calendar");

        w.GetVisualDescendants().OfType<CalendarView>().Should().ContainSingle();
        w.GetVisualDescendants().OfType<Button>().Should().Contain(b => b.Content as string == "New event");
        vm.Days.Should().HaveCount(42);
        // The grid must actually be materialised, not just present in the ViewModel.
        w.GetVisualDescendants().OfType<TextBlock>()
            .Count(t => t.Text == vm.Days[10].DayNumber.ToString())
            .Should().BeGreaterThan(0, "day numbers must render");
    }

    [AvaloniaFact]
    public void EventEditor_RendersFields()
    {
        var vm = Services.GetRequiredService<EventEditorViewModel>();
        var w = new EventEditorWindow { DataContext = vm };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Shot(w, "event-editor");

        w.GetVisualDescendants().OfType<Button>().Should().Contain(b => b.Content as string == "Save");
        w.GetVisualDescendants().OfType<TimePicker>().Should().HaveCount(2);
    }

    [AvaloniaFact]
    public void MainWindow_HasSearchBox_AndAiButtons()
    {
        var vm = Services.GetRequiredService<MainWindowViewModel>();
        var w = new MainWindow { DataContext = vm };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Shot(w, "main-search-ai");

        var buttons = w.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).ToList();
        buttons.Should().Contain("Find");
        buttons.Should().Contain("Summarize");
        buttons.Should().Contain("Classify");
        buttons.Should().Contain("Calendar");
    }

    [AvaloniaFact]
    public void ThemeButton_ActuallyChangesTheThemeVariant()
    {
        var vm = Services.GetRequiredService<MainWindowViewModel>();
        var w = new MainWindow { DataContext = vm };
        w.Show();
        Dispatcher.UIThread.RunJobs();

        var before = Application.Current!.RequestedThemeVariant;
        vm.ToggleThemeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Application.Current.RequestedThemeVariant.Should().NotBe(before,
            "the Theme button used to toggle a property nothing was bound to");
        vm.IsDarkTheme.Should().Be(Application.Current.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Dark);

        vm.ToggleThemeCommand.Execute(null); // back to the original variant for other tests
        Dispatcher.UIThread.RunJobs();
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

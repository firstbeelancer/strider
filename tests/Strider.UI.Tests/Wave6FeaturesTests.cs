using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Strider.Core.Domain;
using Strider.UI;
using Strider.UI.Rendering;
using Strider.UI.ViewModels;
using Strider.UI.Views;

namespace Strider.UI.Tests;

/// <summary>
/// Wave 6 acceptance: fonts & colours, the HTML signature and the coloured flags
/// with their rules. These tests go through the real ViewModels and the real
/// XAML — the previous waves shipped several "bound to nothing" UI bugs, so the
/// assertions look at rendered controls, not only at ViewModel properties.
/// </summary>
public class Wave6FeaturesTests
{
    private static IServiceProvider Services => ((App)Application.Current!).Services;

    // === Composer: colours and fonts over a selection ===

    [AvaloniaFact]
    public void Composer_ColoursOnlyTheSelectedWords()
    {
        var vm = Services.GetRequiredService<ComposeViewModel>();
        vm.BodyPlain = "Hello world";

        vm.SelectionStart = 6;
        vm.SelectionLength = 5;
        vm.ApplyColorCommand.Execute("#E5484D");

        var html = vm.PreviewHtml;
        html.Should().Contain("<span style=\"color:#E5484D;\">world</span>");
        html.Should().Contain("Hello");
        html.Should().NotContain("Hello world<span", "only the selection is wrapped");
    }

    [AvaloniaFact]
    public void Composer_HighlightsAndFontsAndBold_StackOnTheSameRange()
    {
        var vm = Services.GetRequiredService<ComposeViewModel>();
        vm.BodyPlain = "Hello world";

        vm.SelectionStart = 0;
        vm.SelectionLength = 5;
        vm.ApplyHighlightCommand.Execute("#FFF3B0");
        vm.SelectionStart = 0;
        vm.SelectionLength = 5;
        vm.BoldCommand.Execute(null);
        vm.SelectionStart = 0;
        vm.SelectionLength = 5;
        vm.ApplyFontFamilyCommand.Execute("Georgia");

        var html = vm.PreviewHtml;
        html.Should().Contain("background-color:#FFF3B0");
        html.Should().Contain("font-family:'Georgia'");
        html.Should().Contain("font-weight:bold");
        html.Should().Contain("Hello");
    }

    [AvaloniaFact]
    public void Composer_WithoutSelection_ChangesTheDefaultColourInstead()
    {
        var vm = Services.GetRequiredService<ComposeViewModel>();
        vm.BodyPlain = "Hello world";
        vm.SelectionStart = 0;
        vm.SelectionLength = 0;

        vm.ApplyColorCommand.Execute("#3E63DD");

        vm.TextStyle.TextColor.Should().Be("#3E63DD");
        vm.PreviewHtml.Should().Contain("color:#3E63DD");
    }

    [AvaloniaFact]
    public void Composer_BoldThenClear_RemovesTheStyling()
    {
        var vm = Services.GetRequiredService<ComposeViewModel>();
        vm.BodyPlain = "Hello world";
        vm.SelectionStart = 0;
        vm.SelectionLength = 5;

        vm.BoldCommand.Execute(null);
        vm.PreviewHtml.Should().Contain("font-weight:bold");

        vm.ClearFormattingCommand.Execute(null);
        vm.PreviewHtml.Should().NotContain("font-weight:bold");
    }

    // === Composer: HTML signature ===

    [AvaloniaFact]
    public void Composer_HtmlSignature_IsInlinedInTheMessage()
    {
        var vm = Services.GetRequiredService<ComposeViewModel>();
        vm.BodyPlain = "Hello";
        vm.SelectedSignature = new Signature
        {
            Name = "Work",
            ContentHtml = "<div><b>Anna</b><br><img src=\"cid:logo\" alt=\"logo\"></div>",
        };

        vm.PreviewHtml.Should().Contain("<b>Anna</b>");
        vm.SignatureAsAttachment.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Composer_SignatureAsFile_LeavesTheBodyClean()
    {
        var vm = Services.GetRequiredService<ComposeViewModel>();
        vm.BodyPlain = "Hello";
        vm.SelectedSignature = new Signature
        {
            Name = "Work",
            ContentHtml = "<b>Anna</b>",
            AttachAsHtmlFile = true,
        };

        vm.SignatureAsAttachment.Should().BeTrue("the signature itself asks to go out as a file");
        vm.SignaturePreviewHtml.Should().BeEmpty();
        vm.PreviewHtml.Should().NotContain("<b>Anna</b>");
    }

    // === Reader: HTML renders with its colours and fonts ===

    [AvaloniaFact]
    public void Reader_HtmlBody_RendersColourAndFontInProcess()
    {
        var block = new HtmlTextBlock
        {
            Html = "<p>Plain <span style=\"color:#E5484D;font-family:'Georgia'\">red serif</span> and <b>bold</b></p>",
        };
        var window = new Window { Content = block };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var runs = block.Inlines!.OfType<Avalonia.Controls.Documents.Run>().ToList();
        runs.Should().Contain(r => r.Text == "red serif");
        var red = runs.First(r => r.Text == "red serif");
        red.Foreground.Should().BeOfType<SolidColorBrush>()
            .Which.Color.Should().Be(Color.Parse("#E5484D"));
        red.FontFamily.Name.Should().Be("Georgia");
        runs.Should().Contain(r => r.Text == "bold" && r.FontWeight == FontWeight.Bold);
    }

    // === Message list: coloured flag bar and coloured subject ===

    [AvaloniaFact]
    public void MessageList_FlaggedRow_PaintsTheSubjectInTheFlagColour()
    {
        var vm = Services.GetRequiredService<MainWindowViewModel>();
        var flagged = new Message
        {
            Subject = "Invoice 42",
            FromName = "Billing",
            DateUtc = DateTime.UtcNow,
            FlagColor = FlagColor.Blue,
        };
        vm.MessageList.Messages = new System.Collections.ObjectModel.ObservableCollection<Message>(new[] { flagged });

        var w = new MainWindow { DataContext = vm };
        w.Show();
        Dispatcher.UIThread.RunJobs();

        flagged.HasFlagColor.Should().BeTrue();
        flagged.FlagColorHex.Should().Be("#3E63DD");

        var subjectBlocks = w.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Text == "Invoice 42")
            .ToList();
        subjectBlocks.Should().NotBeEmpty("the subject must render");

        var coloured = subjectBlocks
            .Select(t => t.Foreground)
            .OfType<SolidColorBrush>()
            .ToList();
        coloured.Should().Contain(b => b.Color == Color.Parse("#3E63DD"),
            "the flagged subject is painted in the flag colour");
    }

    [AvaloniaFact]
    public async Task MessageList_SetFlag_StoresTheColourAndMarksItManual()
    {
        var vm = Services.GetRequiredService<MainWindowViewModel>();
        var message = new Message { Subject = "From the boss", DateUtc = DateTime.UtcNow };
        vm.MessageList.Messages = new System.Collections.ObjectModel.ObservableCollection<Message>(new[] { message });
        vm.MessageList.SelectedMessage = message;

        await vm.MessageList.SetFlagCommand.ExecuteAsync(FlagColor.Blue);

        message.FlagColor.Should().Be(FlagColor.Blue);
        message.FlagColorIsAutomatic.Should().BeFalse("a manual flag must survive later rule runs");
        message.HasFlagColor.Should().BeTrue();
    }

    // === Settings: the two new tabs are real, bound UI ===

    [AvaloniaFact]
    public async Task Settings_FontsTab_HasFontListSizeColoursAndPreview()
    {
        var vm = Services.GetRequiredService<SettingsViewModel>();
        var s = new SettingsWindow { DataContext = vm };
        s.Show();
        await vm.LoadCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var tabs = s.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedIndex = TabIndex(tabs, "Fonts & Text");
        Dispatcher.UIThread.RunJobs();

        s.GetVisualDescendants().OfType<HtmlTextBlock>().Should().NotBeEmpty(
            "the style preview is the only way to see the chosen font and colour");
        s.GetVisualDescendants().OfType<Button>().Should().Contain(b => b.Content as string == "Add font...");
        s.GetVisualDescendants().OfType<Button>().Should().Contain(b => b.Content as string == "Save text style");
        vm.FontSizes.Should().Contain(14);
        vm.TextStylePreview.Should().Contain("font-family");
    }

    [AvaloniaFact]
    public async Task Settings_FlagsTab_HasRuleEditorAndPalette()
    {
        var vm = Services.GetRequiredService<SettingsViewModel>();
        var s = new SettingsWindow { DataContext = vm };
        s.Show();
        await vm.LoadCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var tabs = s.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedIndex = TabIndex(tabs, "Flags");
        Dispatcher.UIThread.RunJobs();

        var buttons = s.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).ToList();
        buttons.Should().Contain("New rule");
        buttons.Should().Contain("Save rule");
        buttons.Should().Contain("Apply now");

        // The colour palette must be bound to the seven flag colours.
        vm.RuleColors.Should().HaveCount(7);
        vm.RuleFields.Should().Contain(Strider.Core.Domain.FlagRuleField.Sender);
    }

    private static int TabIndex(TabControl tabs, string header)
    {
        for (var i = 0; i < tabs.ItemCount; i++)
        {
            if (tabs.Items[i] is TabItem item && item.Header as string == header) return i;
        }

        return 0;
    }
}

using System.Globalization;
using System.Windows;
using FluentAssertions;
using LalabAutoReport.UI.Converters;
using Xunit;

namespace LalabAutoReport.Tests;

public class ConverterTests
{
    [Fact]
    public void BoolToVisibilityConverter_DefaultBehavior_MatchesStandardWpf()
    {
        var converter = new BoolToVisibilityConverter();

        converter.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Visible);

        converter.Convert(false, typeof(Visibility), null, CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Collapsed);

        converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Collapsed);
    }

    [Theory]
    [InlineData("Inverse")]
    [InlineData("inverse")]
    [InlineData("INVERSE")]
    [InlineData("Invert")]
    [InlineData("invert")]
    [InlineData("Not")]
    [InlineData("!")]
    public void BoolToVisibilityConverter_WithInverseParameter_InvertsCorrectly(string param)
    {
        var converter = new BoolToVisibilityConverter();

        // When HasFolders is FALSE (no folders), with "Inverse" it must be VISIBLE
        converter.Convert(false, typeof(Visibility), param, CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Visible);

        // When HasFolders is TRUE (folders added), with "Inverse" it must be COLLAPSED
        converter.Convert(true, typeof(Visibility), param, CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void InverseBooleanConverter_InvertsCorrectly()
    {
        var converter = new InverseBooleanConverter();

        converter.Convert(true, typeof(bool), null, CultureInfo.InvariantCulture)
            .Should().Be(false);

        converter.Convert(false, typeof(bool), null, CultureInfo.InvariantCulture)
            .Should().Be(true);

        converter.Convert(null, typeof(bool), null, CultureInfo.InvariantCulture)
            .Should().Be(true);
    }

    [Fact]
    public void NumberThousandsConverter_Convert_FormatsNumbersWithCommas()
    {
        var converter = new NumberThousandsConverter();

        converter.Convert(0L, typeof(string), null, CultureInfo.InvariantCulture)
            .Should().Be("0");

        converter.Convert(15000L, typeof(string), null, CultureInfo.InvariantCulture)
            .Should().Be("15,000");

        converter.Convert(1500000L, typeof(string), null, CultureInfo.InvariantCulture)
            .Should().Be("1,500,000");

        converter.Convert(25, typeof(string), null, CultureInfo.InvariantCulture)
            .Should().Be("25");

        converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture)
            .Should().Be(string.Empty);
    }

    [Fact]
    public void NumberThousandsConverter_Convert_WithZeroAsEmpty_ReturnsEmptyWhenZero()
    {
        var converter = new NumberThousandsConverter();

        converter.Convert(0L, typeof(string), "ZeroAsEmpty", CultureInfo.InvariantCulture)
            .Should().Be(string.Empty);

        converter.Convert(0, typeof(string), "ZeroAsEmpty", CultureInfo.InvariantCulture)
            .Should().Be(string.Empty);

        converter.Convert(50000L, typeof(string), "ZeroAsEmpty", CultureInfo.InvariantCulture)
            .Should().Be("50,000");
    }

    [Fact]
    public void NumberThousandsConverter_ConvertBack_ParsesFormattedStringsAndEmptyGracefully()
    {
        var converter = new NumberThousandsConverter();

        // Empty or whitespace converts to 0 / 0L safely without throwing
        converter.ConvertBack("", typeof(long), null, CultureInfo.InvariantCulture)
            .Should().Be(0L);

        converter.ConvertBack("   ", typeof(long), null, CultureInfo.InvariantCulture)
            .Should().Be(0L);

        converter.ConvertBack("", typeof(int), null, CultureInfo.InvariantCulture)
            .Should().Be(0);

        // Formatted thousands
        converter.ConvertBack("15,000", typeof(long), null, CultureInfo.InvariantCulture)
            .Should().Be(15000L);

        converter.ConvertBack("1,500,000", typeof(long), null, CultureInfo.InvariantCulture)
            .Should().Be(1500000L);

        converter.ConvertBack("250", typeof(int), null, CultureInfo.InvariantCulture)
            .Should().Be(250);

        // Zeros
        converter.ConvertBack("0", typeof(long), null, CultureInfo.InvariantCulture)
            .Should().Be(0L);

        converter.ConvertBack("000", typeof(long), null, CultureInfo.InvariantCulture)
            .Should().Be(0L);
    }

    [Fact]
    public void NumericInputBehavior_WhenTextIsZero_TypingDigitReplacesZero()
    {
        var thread = new System.Threading.Thread(() =>
        {
            var textBox = new System.Windows.Controls.TextBox();
            LalabAutoReport.UI.Behaviors.NumericInputBehavior.SetIsThousands(textBox, true);
            textBox.Text = "0";
            textBox.CaretIndex = 1; // Caret after 0 as user clicks

            var textArgs = new System.Windows.Input.TextCompositionEventArgs(
                System.Windows.Input.InputManager.Current.PrimaryKeyboardDevice,
                new System.Windows.Input.TextComposition(System.Windows.Input.InputManager.Current, textBox, "3"))
            {
                RoutedEvent = System.Windows.UIElement.PreviewTextInputEvent
            };
            textBox.RaiseEvent(textArgs);

            // Typing '3' replaces '0' directly!
            textBox.Text.Should().Be("3");
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void NumericInputBehavior_WhenTextIsZero_BackspaceClearsToEmpty()
    {
        var thread = new System.Threading.Thread(() =>
        {
            var textBox = new System.Windows.Controls.TextBox();
            LalabAutoReport.UI.Behaviors.NumericInputBehavior.SetIsThousands(textBox, true);
            textBox.Text = "0";

            var keyArgs = new System.Windows.Input.KeyEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice,
                new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "", System.IntPtr.Zero),
                0,
                System.Windows.Input.Key.Back)
            {
                RoutedEvent = System.Windows.UIElement.PreviewKeyDownEvent
            };
            textBox.RaiseEvent(keyArgs);

            textBox.Text.Should().Be(string.Empty);
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void NumericInputBehavior_OnFocus_SelectsAllText()
    {
        var thread = new System.Threading.Thread(() =>
        {
            var textBox = new System.Windows.Controls.TextBox();
            LalabAutoReport.UI.Behaviors.NumericInputBehavior.SetIsThousands(textBox, true);
            textBox.Text = "50,000";

            var e = new System.Windows.Input.KeyboardFocusChangedEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice,
                0,
                null,
                textBox)
            {
                RoutedEvent = System.Windows.UIElement.GotKeyboardFocusEvent
            };
            textBox.RaiseEvent(e);

            textBox.SelectionLength.Should().Be("50,000".Length);
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}

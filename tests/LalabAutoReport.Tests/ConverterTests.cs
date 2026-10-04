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
            try
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
            }
            finally
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
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
            try
            {
                var textBox = new System.Windows.Controls.TextBox();
                LalabAutoReport.UI.Behaviors.NumericInputBehavior.SetIsThousands(textBox, true);
                textBox.Text = "0";

                using var hwndSource = new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "", System.IntPtr.Zero);
                var keyArgs = new System.Windows.Input.KeyEventArgs(
                    System.Windows.Input.Keyboard.PrimaryDevice,
                    hwndSource,
                    0,
                    System.Windows.Input.Key.Back)
                {
                    RoutedEvent = System.Windows.UIElement.PreviewKeyDownEvent
                };
                textBox.RaiseEvent(keyArgs);

                textBox.Text.Should().Be(string.Empty);
            }
            finally
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
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
            try
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
            }
            finally
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void CustomersView_InitializeComponent_DoesNotThrowXamlParseException()
    {
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var app = System.Windows.Application.Current ?? new System.Windows.Application();
                bool hasTokens = false;
                bool hasStyles = false;
                foreach (var md in app.Resources.MergedDictionaries)
                {
                    if (md.Source?.OriginalString?.Contains("TinixTokens.xaml") == true) hasTokens = true;
                    if (md.Source?.OriginalString?.Contains("TinixStyles.xaml") == true) hasStyles = true;
                }

                if (!hasTokens)
                {
                    app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
                    {
                        Source = new System.Uri("pack://application:,,,/LalabAutoReport.UI;component/Resources/TinixTokens.xaml")
                    });
                }
                if (!hasStyles)
                {
                    app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
                    {
                        Source = new System.Uri("pack://application:,,,/LalabAutoReport.UI;component/Resources/TinixStyles.xaml")
                    });
                }

                if (!app.Resources.Contains("InverseBoolConverter"))
                    app.Resources["InverseBoolConverter"] = new LalabAutoReport.UI.Converters.InverseBooleanConverter();
                if (!app.Resources.Contains("NumberThousandsConverter"))
                    app.Resources["NumberThousandsConverter"] = new LalabAutoReport.UI.Converters.NumberThousandsConverter();
                if (!app.Resources.Contains("BoolToVis"))
                    app.Resources["BoolToVis"] = new LalabAutoReport.UI.Converters.BoolToVisibilityConverter();
                if (!app.Resources.Contains("EnumToBoolConverter"))
                    app.Resources["EnumToBoolConverter"] = new LalabAutoReport.UI.Converters.EnumToBooleanConverter();

                var view = new LalabAutoReport.UI.Views.CustomersView();
                view.Should().NotBeNull();
            }
            finally
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void EnumToBooleanConverter_Convert_And_ConvertBack_WorksCorrectly()
    {
        var converter = new LalabAutoReport.UI.Converters.EnumToBooleanConverter();

        // Convert
        converter.Convert(LalabAutoReport.Core.Domain.PriceTier.Retail, typeof(bool), "Retail", System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(true);
        converter.Convert(LalabAutoReport.Core.Domain.PriceTier.Retail, typeof(bool), "Studio", System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(false);
        converter.Convert(LalabAutoReport.Core.Domain.PriceTier.Vip, typeof(bool), "Vip", System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(true);
        converter.Convert(null, typeof(bool), "Retail", System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(false);

        // ConvertBack true
        converter.ConvertBack(true, typeof(LalabAutoReport.Core.Domain.PriceTier), "Studio", System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(LalabAutoReport.Core.Domain.PriceTier.Studio);
        converter.ConvertBack(true, typeof(LalabAutoReport.Core.Domain.PriceTier), "Vip", System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(LalabAutoReport.Core.Domain.PriceTier.Vip);

        // ConvertBack false returns Binding.DoNothing
        converter.ConvertBack(false, typeof(LalabAutoReport.Core.Domain.PriceTier), "Retail", System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(System.Windows.Data.Binding.DoNothing);
    }
}


using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LalabAutoReport.UI.Behaviors;

public static class NumericInputBehavior
{
    public static readonly DependencyProperty IsThousandsProperty =
        DependencyProperty.RegisterAttached(
            "IsThousands",
            typeof(bool),
            typeof(NumericInputBehavior),
            new UIPropertyMetadata(false, OnIsThousandsChanged));

    public static bool GetIsThousands(DependencyObject obj) => (bool)obj.GetValue(IsThousandsProperty);
    public static void SetIsThousands(DependencyObject obj, bool value) => obj.SetValue(IsThousandsProperty, value);

    public static readonly DependencyProperty AutoClearZeroProperty =
        DependencyProperty.RegisterAttached(
            "AutoClearZero",
            typeof(bool),
            typeof(NumericInputBehavior),
            new UIPropertyMetadata(true));

    public static bool GetAutoClearZero(DependencyObject obj) => (bool)obj.GetValue(AutoClearZeroProperty);
    public static void SetAutoClearZero(DependencyObject obj, bool value) => obj.SetValue(AutoClearZeroProperty, value);

    public static readonly DependencyProperty AllowEmptyProperty =
        DependencyProperty.RegisterAttached(
            "AllowEmpty",
            typeof(bool),
            typeof(NumericInputBehavior),
            new UIPropertyMetadata(false));

    public static bool GetAllowEmpty(DependencyObject obj) => (bool)obj.GetValue(AllowEmptyProperty);
    public static void SetAllowEmpty(DependencyObject obj, bool value) => obj.SetValue(AllowEmptyProperty, value);

    private static void OnIsThousandsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox) return;

        if ((bool)e.NewValue)
        {
            textBox.Loaded += TextBox_Loaded;
            textBox.PreviewTextInput += TextBox_PreviewTextInput;
            textBox.PreviewKeyDown += TextBox_PreviewKeyDown;
            textBox.PreviewMouseLeftButtonDown += TextBox_PreviewMouseLeftButtonDown;
            textBox.TextChanged += TextBox_TextChanged;
            textBox.GotKeyboardFocus += TextBox_GotKeyboardFocus;
            textBox.LostFocus += TextBox_LostFocus;
            DataObject.AddPastingHandler(textBox, TextBox_Pasting);
        }
        else
        {
            textBox.Loaded -= TextBox_Loaded;
            textBox.PreviewTextInput -= TextBox_PreviewTextInput;
            textBox.PreviewKeyDown -= TextBox_PreviewKeyDown;
            textBox.PreviewMouseLeftButtonDown -= TextBox_PreviewMouseLeftButtonDown;
            textBox.TextChanged -= TextBox_TextChanged;
            textBox.GotKeyboardFocus -= TextBox_GotKeyboardFocus;
            textBox.LostFocus -= TextBox_LostFocus;
            DataObject.RemovePastingHandler(textBox, TextBox_Pasting);
        }
    }

    private static void TextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            if (GetAllowEmpty(textBox) && string.IsNullOrWhiteSpace(textBox.Text))
            {
                return;
            }
            FormatTextBox(textBox);
        }
    }

    private static void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox) return;

        if (!textBox.IsKeyboardFocusWithin)
        {
            textBox.Focus();
            textBox.SelectAll();
            textBox.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (textBox.IsKeyboardFocusWithin)
                {
                    textBox.SelectAll();
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
            e.Handled = true;
        }
    }

    private static void TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        HandleFocus(textBox);
    }

    private static void HandleFocus(TextBox textBox)
    {
        textBox.SelectAll();
        textBox.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (textBox.IsKeyboardFocusWithin)
            {
                textBox.SelectAll();
            }
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private static void TextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                if (!GetAllowEmpty(textBox))
                {
                    textBox.Text = "0";
                }
            }
            FormatTextBox(textBox);
        }
    }

    private static void TextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox textBox) return;

        if (!e.Text.All(char.IsDigit))
        {
            e.Handled = true;
            return;
        }

        // Auto-replace zero: if current text is "0" (or whitespace) and nothing is selected, replace "0" directly with the typed digit
        if ((textBox.Text.Trim() == "0" || string.IsNullOrWhiteSpace(textBox.Text)) && textBox.SelectionLength == 0)
        {
            textBox.Text = e.Text;
            textBox.CaretIndex = e.Text.Length;
            e.Handled = true;
            return;
        }
    }

    private static void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;

        if (e.Key == Key.Space)
        {
            e.Handled = true;
            return;
        }

        // Handle Backspace or Delete when text is just "0"
        if ((e.Key == Key.Back || e.Key == Key.Delete) && textBox.Text.Trim() == "0")
        {
            textBox.Text = string.Empty;
            e.Handled = true;
            return;
        }

        // Handle Backspace when cursor is immediately after a comma: e.g. "400,|000"
        if (e.Key == Key.Back && textBox.SelectionLength == 0 && textBox.CaretIndex > 0)
        {
            if (textBox.CaretIndex > 1 && textBox.Text[textBox.CaretIndex - 1] == ',')
            {
                int removePos = textBox.CaretIndex - 2;
                textBox.Text = textBox.Text.Remove(removePos, 1);
                textBox.CaretIndex = removePos;
                e.Handled = true;
                return;
            }
        }

        // Handle Delete when cursor is immediately before a comma: e.g. "400|,000"
        if (e.Key == Key.Delete && textBox.SelectionLength == 0 && textBox.CaretIndex < textBox.Text.Length)
        {
            if (textBox.Text[textBox.CaretIndex] == ',')
            {
                if (textBox.CaretIndex + 1 < textBox.Text.Length)
                {
                    int removePos = textBox.CaretIndex + 1;
                    textBox.Text = textBox.Text.Remove(removePos, 1);
                    e.Handled = true;
                    return;
                }
            }
        }
    }

    private static void TextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox) return;

        if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText, true))
        {
            e.CancelCommand();
            return;
        }

        var text = e.DataObject.GetData(DataFormats.UnicodeText, true) as string;
        if (string.IsNullOrEmpty(text))
        {
            e.CancelCommand();
            return;
        }

        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(digits))
        {
            e.CancelCommand();
            return;
        }

        e.CancelCommand();
        int selStart = textBox.SelectionStart;
        int selLength = textBox.SelectionLength;
        textBox.Text = textBox.Text.Remove(selStart, selLength).Insert(selStart, digits);
        textBox.CaretIndex = selStart + digits.Length;
    }

    private static bool _isFormatting;

    private static void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isFormatting) return;
        if (sender is TextBox textBox)
        {
            FormatTextBox(textBox);
        }
    }

    private static void FormatTextBox(TextBox textBox)
    {
        if (_isFormatting) return;

        try
        {
            _isFormatting = true;

            string currentText = textBox.Text;
            if (string.IsNullOrWhiteSpace(currentText))
            {
                return;
            }

            int caret = textBox.CaretIndex;
            int digitsBeforeCaret = currentText.Take(caret).Count(char.IsDigit);

            string digits = new string(currentText.Where(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(digits))
            {
                textBox.Text = string.Empty;
                return;
            }

            // Cap at 15 digits to avoid overflow
            if (digits.Length > 15)
            {
                digits = digits.Substring(0, 15);
            }

            if (long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long parsedValue))
            {
                string formatted = parsedValue.ToString("N0", CultureInfo.InvariantCulture);
                if (formatted != currentText)
                {
                    textBox.Text = formatted;

                    int newCaret = 0;
                    int count = 0;
                    for (int i = 0; i < formatted.Length; i++)
                    {
                        if (char.IsDigit(formatted[i]))
                        {
                            count++;
                        }
                        if (count == digitsBeforeCaret)
                        {
                            newCaret = i + 1;
                            break;
                        }
                    }

                    if (digitsBeforeCaret == 0)
                    {
                        newCaret = 0;
                    }
                    else if (count < digitsBeforeCaret)
                    {
                        newCaret = formatted.Length;
                    }

                    textBox.CaretIndex = Math.Min(newCaret, formatted.Length);
                }
            }
        }
        finally
        {
            _isFormatting = false;
        }
    }
}

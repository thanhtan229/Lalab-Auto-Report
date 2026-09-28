using System.Windows;
using System.Windows.Controls;
using LalabAutoReport.UI.ViewModels;

namespace LalabAutoReport.UI.Views;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
    }

    private ReportsViewModel? ViewModel => DataContext as ReportsViewModel;

    private void ModeMonthly_Checked(object sender, RoutedEventArgs e)
    {
        if (MonthlyControls != null) MonthlyControls.Visibility = Visibility.Visible;
        if (DailyControls != null) DailyControls.Visibility = Visibility.Collapsed;
        if (DateRangeControls != null) DateRangeControls.Visibility = Visibility.Collapsed;
        if (ViewModel != null) ViewModel.ReportMode = "Monthly";
    }

    private void ModeDaily_Checked(object sender, RoutedEventArgs e)
    {
        if (MonthlyControls != null) MonthlyControls.Visibility = Visibility.Collapsed;
        if (DailyControls != null) DailyControls.Visibility = Visibility.Visible;
        if (DateRangeControls != null) DateRangeControls.Visibility = Visibility.Collapsed;
        if (ViewModel != null) ViewModel.ReportMode = "Daily";
    }

    private void ModeDateRange_Checked(object sender, RoutedEventArgs e)
    {
        if (MonthlyControls != null) MonthlyControls.Visibility = Visibility.Collapsed;
        if (DailyControls != null) DailyControls.Visibility = Visibility.Collapsed;
        if (DateRangeControls != null) DateRangeControls.Visibility = Visibility.Visible;
        if (ViewModel != null) ViewModel.ReportMode = "DateRange";
    }
}

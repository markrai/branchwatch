using System.Globalization;
using System.Windows;

namespace BranchWatch;

public partial class StickyNoteAlarmDialog : Window
{
    internal DateTimeOffset? SelectedDueUtc { get; private set; }
    internal bool RemoveAlarm { get; private set; }

    internal StickyNoteAlarmDialog(DateTimeOffset? existing, bool ringing)
    {
        InitializeComponent();
        var now = DateTimeOffset.Now;
        var initial = existing is { } due && due > now && !ringing
            ? due.LocalDateTime : StickyNoteAlarmTime.NextMinute(now.LocalDateTime);
        AlarmCalendar.DisplayDateStart = now.Date;
        AlarmCalendar.DisplayDate = initial.Date;
        AlarmCalendar.SelectedDate = initial.Date;
        HourPicker.ItemsSource = Enumerable.Range(1, 12).Select(i => i.ToString("00", CultureInfo.InvariantCulture)).ToArray();
        MinutePicker.ItemsSource = Enumerable.Range(0, 60).Select(i => i.ToString("00", CultureInfo.InvariantCulture)).ToArray();
        PeriodPicker.ItemsSource = new[] { "AM", "PM" };
        var clock = StickyNoteAlarmTime.ToTwelveHour(initial.Hour);
        HourPicker.SelectedIndex = clock.Hour - 1;
        PeriodPicker.SelectedIndex = clock.IsPm ? 1 : 0;
        MinutePicker.SelectedIndex = initial.Minute;
        RemoveButton.Visibility = existing.HasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetClick(object sender, RoutedEventArgs e)
    {
        if (AlarmCalendar.SelectedDate is not { } date)
        {
            ValidationText.Text = "Choose a date.";
            return;
        }
        if (!StickyNoteAlarmTime.TryResolveTwelveHour(date, HourPicker.SelectedIndex + 1, MinutePicker.SelectedIndex,
            PeriodPicker.SelectedItem as string, TimeZoneInfo.Local, DateTimeOffset.UtcNow, out var due, out var error))
        {
            ValidationText.Text = error;
            return;
        }
        SelectedDueUtc = due;
        DialogResult = true;
    }

    private void RemoveClick(object sender, RoutedEventArgs e)
    {
        RemoveAlarm = true;
        DialogResult = true;
    }
}

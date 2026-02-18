using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace AdStats
{
    public partial class MainWindow : Window
    {
        private const string DatabaseFileName = "adstats_db.txt";

        private readonly ObservableCollection<Campaign> _campaigns = new ObservableCollection<Campaign>();
        private readonly List<Campaign> _filtered = new List<Campaign>();
        private readonly string _databasePath;

        public MainWindow()
        {
            InitializeComponent();
            
            _databasePath = Path.Combine(FindProjectRoot(), DatabaseFileName);
            TextDbPath.Text = $"База: {_databasePath}";

            LoadFromFile();
            RebindGrid();
            RefreshKpis();

            TextStatus.Text = "Приложение запущено. Данные загружены.";
        }

        private static string FindProjectRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AdStats.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return Directory.GetCurrentDirectory();
        }

        private void AddCampaign_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadForm(out var candidate))
            {
                return;
            }

            if (_campaigns.Any(c => c.Name.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase)))
            {
                TextStatus.Text = "Кампания с таким названием уже существует.";
                return;
            }

            _campaigns.Add(candidate);
            ApplyFilters();
            RefreshKpis();
            SaveToFile();
            ClearForm();

            TextStatus.Text = $"Кампания '{candidate.Name}' успешно добавлена.";
        }

        private void UpdateCampaign_Click(object sender, RoutedEventArgs e)
        {
            if (!(CampaignGrid.SelectedItem is Campaign selected))
            {
                TextStatus.Text = "Выберите кампанию в таблице для обновления.";
                return;
            }

            if (!TryReadForm(out var updated))
            {
                return;
            }

            var duplicateNameExists = _campaigns
                .Where(c => !ReferenceEquals(c, selected))
                .Any(c => c.Name.Equals(updated.Name, StringComparison.OrdinalIgnoreCase));

            if (duplicateNameExists)
            {
                TextStatus.Text = "Невозможно обновить: найден дубликат названия.";
                return;
            }

            selected.UpdateFrom(updated);
            ApplyFilters();
            RefreshKpis();
            SaveToFile();

            TextStatus.Text = $"Кампания '{selected.Name}' обновлена.";
        }

        private void DeleteCampaign_Click(object sender, RoutedEventArgs e)
        {
            if (!(CampaignGrid.SelectedItem is Campaign selected))
            {
                TextStatus.Text = "Выберите кампанию, которую нужно удалить.";
                return;
            }

            _campaigns.Remove(selected);
            ApplyFilters();
            RefreshKpis();
            SaveToFile();
            ClearForm();

            TextStatus.Text = "Кампания удалена.";
        }

        private void SaveToFile_Click(object sender, RoutedEventArgs e)
        {
            SaveToFile();
            TextStatus.Text = "Данные сохранены вручную.";
        }

        private void ClearForm_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
            TextStatus.Text = "Форма очищена.";
        }

        private void Forecast_Click(object sender, RoutedEventArgs e)
        {
            if (_campaigns.Count == 0)
            {
                TextForecast.Text = "Добавьте хотя бы одну кампанию, чтобы построить прогноз.";
                return;
            }

            if (!TryParseDouble(InputBudgetGrowth.Text, out var growthPercent) || growthPercent < -100)
            {
                TextStatus.Text = "Введите корректный процент роста бюджета (не меньше -100).";
                return;
            }

            var growthFactor = 1 + growthPercent / 100.0;
            var totalBudget = _campaigns.Sum(c => c.Budget);
            var totalRevenue = _campaigns.Sum(c => c.Revenue);
            var currentRoi = totalBudget > 0 ? (totalRevenue - totalBudget) / totalBudget * 100 : 0;

            var projectedBudget = totalBudget * growthFactor;
            var projectedRevenue = totalRevenue * (1 + (growthPercent / 100.0 * 0.72));
            var projectedRoi = projectedBudget > 0 ? (projectedRevenue - projectedBudget) / projectedBudget * 100 : 0;

            TextForecast.Text =
                $"Прогноз при изменении бюджета на {growthPercent:F1}%:\n" +
                $"• Новый бюджет: {projectedBudget:N0} ₽\n" +
                $"• Прогноз выручки: {projectedRevenue:N0} ₽\n" +
                $"• ROI: {currentRoi:F2}% → {projectedRoi:F2}%";

            TextStatus.Text = "Прогноз рассчитан.";
        }

        private void CampaignGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(CampaignGrid.SelectedItem is Campaign selected))
            {
                return;
            }

            InputName.Text = selected.Name;
            SetComboItem(InputChannel, selected.Channel);
            SetComboItem(InputStatus, selected.Status);
            InputBudget.Text = selected.Budget.ToString(CultureInfo.InvariantCulture);
            InputImpressions.Text = selected.Impressions.ToString(CultureInfo.InvariantCulture);
            InputClicks.Text = selected.Clicks.ToString(CultureInfo.InvariantCulture);
            InputConversions.Text = selected.Conversions.ToString(CultureInfo.InvariantCulture);
            InputRevenue.Text = selected.Revenue.ToString(CultureInfo.InvariantCulture);

            TextStatus.Text = $"Выбрана кампания: {selected.Name}";
        }

        private void FilterChanged(object sender, EventArgs e)
        {
            ApplyFilters();
            RefreshKpis();
        }

        private void ApplyFilters()
        {
            var text = (InputSearch.Text ?? string.Empty).Trim();
            var channel = GetSelectedComboText(FilterChannel);
            var status = GetSelectedComboText(FilterStatus);

            IEnumerable<Campaign> result = _campaigns;

            if (!string.IsNullOrWhiteSpace(text))
            {
                result = result.Where(c => c.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (!string.Equals(channel, "Все", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Where(c => c.Channel == channel);
            }

            if (!string.Equals(status, "Все", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Where(c => c.Status == status);
            }

            _filtered.Clear();
            _filtered.AddRange(result.OrderByDescending(c => c.RoiPercent));
            RebindGrid();
        }

        private void RebindGrid()
        {
            CampaignGrid.ItemsSource = null;
            CampaignGrid.ItemsSource = _filtered.Count > 0 || HasAnyFilter() ? _filtered : _campaigns.OrderByDescending(c => c.RoiPercent).ToList();
        }

        private bool HasAnyFilter()
        {
            var channel = GetSelectedComboText(FilterChannel);
            var status = GetSelectedComboText(FilterStatus);

            return !string.IsNullOrWhiteSpace(InputSearch.Text)
                   || !string.Equals(channel, "Все", StringComparison.OrdinalIgnoreCase)
                   || !string.Equals(status, "Все", StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshKpis()
        {
            var source = HasAnyFilter() ? _filtered : _campaigns.ToList();

            var totalBudget = source.Sum(c => c.Budget);
            var avgRoi = source.Count > 0 ? source.Average(c => c.RoiPercent) : 0;
            var avgCtr = source.Count > 0 ? source.Average(c => c.CtrPercent) : 0;
            var totalLeads = source.Sum(c => c.Conversions);
            var activeCount = source.Count(c => c.Status == "Active");

            KpiTotalBudget.Text = $"{totalBudget:N0} ₽";
            KpiAvgRoi.Text = $"{avgRoi:F2}%";
            KpiAvgCtr.Text = $"{avgCtr:F2}%";
            KpiTotalConversions.Text = totalLeads.ToString(CultureInfo.InvariantCulture);
            KpiActiveCampaigns.Text = activeCount.ToString(CultureInfo.InvariantCulture);

            TextLastSync.Text = $"Обновлено: {DateTime.Now:dd.MM.yyyy HH:mm:ss}";
        }

        private bool TryReadForm(out Campaign campaign)
        {
            campaign = null;

            var name = (InputName.Text ?? string.Empty).Trim();
            var channel = GetSelectedComboText(InputChannel);
            var status = GetSelectedComboText(InputStatus);

            if (string.IsNullOrWhiteSpace(name))
            {
                TextStatus.Text = "Название кампании обязательно.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(channel))
            {
                TextStatus.Text = "Выберите канал продвижения.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(status))
            {
                TextStatus.Text = "Выберите статус кампании.";
                return false;
            }

            if (!TryParseDouble(InputBudget.Text, out var budget) || budget < 0)
            {
                TextStatus.Text = "Бюджет должен быть числом не меньше 0.";
                return false;
            }

            if (!TryParseInt(InputImpressions.Text, out var impressions) || impressions < 0)
            {
                TextStatus.Text = "Показы должны быть целым числом не меньше 0.";
                return false;
            }

            if (!TryParseInt(InputClicks.Text, out var clicks) || clicks < 0)
            {
                TextStatus.Text = "Клики должны быть целым числом не меньше 0.";
                return false;
            }

            if (!TryParseInt(InputConversions.Text, out var conversions) || conversions < 0)
            {
                TextStatus.Text = "Конверсии должны быть целым числом не меньше 0.";
                return false;
            }

            if (!TryParseDouble(InputRevenue.Text, out var revenue) || revenue < 0)
            {
                TextStatus.Text = "Доход должен быть числом не меньше 0.";
                return false;
            }

            if (clicks > impressions)
            {
                TextStatus.Text = "Клики не могут превышать показы.";
                return false;
            }

            if (conversions > clicks)
            {
                TextStatus.Text = "Конверсии не могут превышать клики.";
                return false;
            }

            campaign = new Campaign(name, channel, status, budget, impressions, clicks, conversions, revenue);
            return true;
        }

        private static bool TryParseDouble(string value, out double result)
        {
            return double.TryParse(value?.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryParseInt(string value, out int result)
        {
            return int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
        }

        private static string GetSelectedComboText(ComboBox comboBox)
        {
            if (comboBox == null)
                return string.Empty;

            if (comboBox.SelectedItem is ComboBoxItem item)
                return item.Content?.ToString() ?? string.Empty;

            return comboBox.Text ?? string.Empty;
        }

        private static void SetComboItem(ComboBox comboBox, string value)
        {
            foreach (var item in comboBox.Items)
            {
                if (item is ComboBoxItem comboItem && string.Equals(comboItem.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = comboItem;
                    return;
                }
            }

            comboBox.SelectedIndex = -1;
        }

        private void LoadFromFile()
        {
            _campaigns.Clear();

            if (!File.Exists(_databasePath))
            {
                File.WriteAllText(_databasePath, "# name|channel|status|budget|impressions|clicks|conversions|revenue\n", Encoding.UTF8);
                return;
            }

            foreach (var line in File.ReadAllLines(_databasePath, Encoding.UTF8))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (Campaign.TryParse(trimmed, out var campaign))
                {
                    _campaigns.Add(campaign);
                }
            }
        }

        private void SaveToFile()
        {
            var lines = new List<string>
            {
                "# name|channel|status|budget|impressions|clicks|conversions|revenue"
            };

            lines.AddRange(_campaigns.Select(c => c.ToStorageLine()));
            File.WriteAllLines(_databasePath, lines, Encoding.UTF8);
        }

        private void ClearForm()
        {
            InputName.Text = string.Empty;
            InputBudget.Text = string.Empty;
            InputImpressions.Text = string.Empty;
            InputClicks.Text = string.Empty;
            InputConversions.Text = string.Empty;
            InputRevenue.Text = string.Empty;
            TextForecast.Text = string.Empty;

            InputChannel.SelectedIndex = -1;
            InputStatus.SelectedIndex = -1;
            CampaignGrid.UnselectAll();
        }

        public class Campaign
        {
            public Campaign(string name, string channel, string status, double budget, int impressions, int clicks, int conversions, double revenue)
            {
                Name = name;
                Channel = channel;
                Status = status;
                Budget = budget;
                Impressions = impressions;
                Clicks = clicks;
                Conversions = conversions;
                Revenue = revenue;
            }

            public string Name { get; private set; }
            public string Channel { get; private set; }
            public string Status { get; private set; }
            public double Budget { get; private set; }
            public int Impressions { get; private set; }
            public int Clicks { get; private set; }
            public int Conversions { get; private set; }
            public double Revenue { get; private set; }

            public double CtrPercent => Impressions == 0 ? 0 : (double)Clicks / Impressions * 100;
            public double Cpc => Clicks == 0 ? 0 : Budget / Clicks;
            public double Cpa => Conversions == 0 ? 0 : Budget / Conversions;
            public double RoiPercent => Budget == 0 ? 0 : (Revenue - Budget) / Budget * 100;

            public void UpdateFrom(Campaign source)
            {
                Name = source.Name;
                Channel = source.Channel;
                Status = source.Status;
                Budget = source.Budget;
                Impressions = source.Impressions;
                Clicks = source.Clicks;
                Conversions = source.Conversions;
                Revenue = source.Revenue;
            }

            public string ToStorageLine()
            {
                return string.Join("|", new[]
                {
                    Escape(Name),
                    Escape(Channel),
                    Escape(Status),
                    Budget.ToString(CultureInfo.InvariantCulture),
                    Impressions.ToString(CultureInfo.InvariantCulture),
                    Clicks.ToString(CultureInfo.InvariantCulture),
                    Conversions.ToString(CultureInfo.InvariantCulture),
                    Revenue.ToString(CultureInfo.InvariantCulture)
                });
            }

            public static bool TryParse(string line, out Campaign campaign)
            {
                campaign = null;
                var parts = line.Split('|');
                if (parts.Length != 8)
                {
                    return false;
                }

                if (!double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var budget)
                    || !int.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var impressions)
                    || !int.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out var clicks)
                    || !int.TryParse(parts[6], NumberStyles.Any, CultureInfo.InvariantCulture, out var conversions)
                    || !double.TryParse(parts[7], NumberStyles.Any, CultureInfo.InvariantCulture, out var revenue))
                {
                    return false;
                }

                if (budget < 0 || impressions < 0 || clicks < 0 || conversions < 0 || revenue < 0 || clicks > impressions || conversions > clicks)
                {
                    return false;
                }

                campaign = new Campaign(Unescape(parts[0]), Unescape(parts[1]), Unescape(parts[2]), budget, impressions, clicks, conversions, revenue);
                return true;
            }

            private static string Escape(string value)
            {
                return (value ?? string.Empty).Replace("|", "/").Trim();
            }

            private static string Unescape(string value)
            {
                return value?.Trim() ?? string.Empty;
            }
        }
    }
}
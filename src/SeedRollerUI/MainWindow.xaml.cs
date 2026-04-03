using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using SeedRollerCli;
using WinForms = System.Windows.Forms;

namespace SeedRollerUI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void StartRoll_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.StartRollAsync(Dispatcher);
    }

    private void CancelRoll_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CancelRoll();
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearLogs();
    }

    private void BrowseGameData_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择 Slay the Spire 2 data 目录"
        };
        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            _viewModel.GameDataPath = dialog.SelectedPath;
        }
    }

    private void BrowseResultJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON 文件 (*.json)|*.json|所有文�?(*.*)|*.*",
            FileName = ResolveDefaultFileName(_viewModel.ResultJsonPath, "seed_results.json")
        };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.ResultJsonPath = dialog.FileName;
        }
    }

    private void AddRelic_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddRelicSelection();
    }

    private void RemoveRelic_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is SelectableOption option)
        {
            _viewModel.RemoveRelicSelection(option);
        }
    }

    private void AddCard_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddCardSelection();
    }

    private void RemoveCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is SelectableOption option)
        {
            _viewModel.RemoveCardSelection(option);
        }
    }

    private void AddPotion_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddPotionSelection();
    }

    private void RemovePotion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is SelectableOption option)
        {
            _viewModel.RemovePotionSelection(option);
        }
    }

    private void LoadConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON/JSONC (*.json;*.jsonc)|*.json;*.jsonc|所有文�?(*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                _viewModel.LoadConfigFromFile(dialog.FileName);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"加载配置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void SaveConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON/JSONC (*.json;*.jsonc)|*.json;*.jsonc|所有文�?(*.*)|*.*",
            FileName = "config.json"
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                var config = _viewModel.ExportConfig();
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var payload = JsonSerializer.Serialize(config, options);
                File.WriteAllText(dialog.FileName, payload);
                _viewModel.AppendLog(RollerLogLevel.Info, $"已保存配置：{dialog.FileName}");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"保存配置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OpenResult_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.TryGetResultPath(out var path) && path != null && File.Exists(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"无法打开文件：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private static string ResolveDefaultFileName(string? current, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(current))
        {
            try
            {
                return Path.GetFileName(current) ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        return fallback;
    }
}

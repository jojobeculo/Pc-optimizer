using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FrameCore.UI.ViewModels;

namespace FrameCore.UI.Views;

public partial class OptimizationView : UserControl
{
    private OptimizationViewModel? _vm;

    public OptimizationView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = e.NewValue as OptimizationViewModel;
            if (_vm != null) _vm.PropertyChanged += OnVmChanged;
        };
    }

    /// <summary>Applying can be started from the top of a long plan: keep the progress, then the outcome, in view.</summary>
    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm == null) return;
        FrameworkElement? target = e.PropertyName switch
        {
            nameof(OptimizationViewModel.ApplyRunning) when _vm.ApplyRunning => ApplyCard,
            nameof(OptimizationViewModel.HasOutcome) when _vm.HasOutcome => OutcomeCard,
            _ => null,
        };
        if (target != null) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => target.BringIntoView());
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using EDSC.Desktop.ViewModels;
using System;
using System.Diagnostics;

namespace EDSC.Desktop.Views
{
    /// <summary>
    /// Code-behind for the button editor: drag a chip and drop it on a category (append)
    /// or on another chip (insert before it).
    /// </summary>
    public partial class ButtonEditorView : UserControl
    {
        private const string DragFormat = "edsc-button-item";
        private const double DragThresholdPixels = 6.0;

        private ButtonItem? _pressedItem;
        private Point _pressedPosition;
        private bool _dragging;

        public ButtonEditorView()
        {
            InitializeComponent();

            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private ButtonEditorViewModel? ViewModel
        {
            get { return DataContext as ButtonEditorViewModel; }
        }

        private void Chip_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control control || control.DataContext is not ButtonItem item)
            {
                return;
            }

            if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            {
                return;
            }

            _pressedItem = item;
            _pressedPosition = e.GetPosition(this);
            _dragging = false;

            ViewModel?.SelectButton(item);
        }

        private async void Chip_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_pressedItem == null || _dragging)
            {
                return;
            }

            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _pressedItem = null;
                return;
            }

            var position = e.GetPosition(this);
            var dx = position.X - _pressedPosition.X;
            var dy = position.Y - _pressedPosition.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < DragThresholdPixels)
            {
                return;
            }

            _dragging = true;
            var item = _pressedItem;

            try
            {
                var data = new DataObject();
                data.Set(DragFormat, item);
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ButtonEditorView] Drag failed: {ex.Message}");
            }
            finally
            {
                _pressedItem = null;
                _dragging = false;
            }
        }

        private void Chip_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_dragging)
            {
                _pressedItem = null;
            }
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = e.Data.Contains(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        }

        private void OnDrop(object? sender, DragEventArgs e)
        {
            if (e.Data.Get(DragFormat) is not ButtonItem item)
            {
                return;
            }

            ButtonItem? before = null;
            ButtonCategory? category = null;

            // Walk up from the element under the pointer to find the chip and the category it sits in
            for (var control = e.Source as Control; control != null; control = control.Parent as Control)
            {
                if (before == null && control.DataContext is ButtonItem chipItem && !ReferenceEquals(chipItem, item))
                {
                    before = chipItem;
                }

                if (control.DataContext is ButtonCategory target)
                {
                    category = target;
                    break;
                }
            }

            if (category == null)
            {
                return;
            }

            ViewModel?.MoveButton(item, category, before);
            e.Handled = true;
        }

        private void RemoveCategory_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control control && control.DataContext is ButtonCategory category)
            {
                ViewModel?.RemoveEmptyCategory(category);
            }
        }
    }
}

using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.System;

namespace CodeEditorControl_WinUI;

public partial class CodeWriter : UserControl, INotifyPropertyChanged
{
 private Point middleClickScrollingStartPoint;
 private Point middleClickScrollingEndPoint;

 [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT lpPoint);
 [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);

 [StructLayout(LayoutKind.Sequential)]
 public struct POINT { public int X; public int Y; }

 public static bool IsLeftMouseButtonDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0;

 /// <summary>Scrolls the view so that the given line is centered.</summary>
 public void ScrollToLine(int iLine) => VerticalScroll.Value = (iLine + 1) * CharHeight - TextControl.ActualHeight / 2;

 /// <summary>Centers the view on the current selection.</summary>
 public void CenterView()
 {
  HorizontalScroll.Value = 0;
  VerticalScroll.Value = (Selection.VisualStart.iLine + 1) * CharHeight - TextControl.ActualHeight / 2;
  Focus(FocusState.Keyboard);
 }

 private async void Scroll_SizeChanged(object sender, SizeChangedEventArgs e)
 {
  if (isCanvasLoaded) await DrawText(false, true);
 }

 private void ScrollContent_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
 {
  if (e.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Touch) return;
  FontSize = Math.Min(Math.Max((int)(startFontsize * e.Cumulative.Scale), MinFontSize), MaxFontSize);
  HorizontalScroll.Value -= e.Delta.Translation.X;
  VerticalScroll.Value -= e.Delta.Translation.Y;
 }

 private void ScrollContent_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e) => startFontsize = FontSize;

 private void Scroll_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
 {
  try
  {
	var pointer = e.GetCurrentPoint(Scroll);
	int mwd = pointer.Properties.MouseWheelDelta;
	if (e.KeyModifiers == VirtualKeyModifiers.Control)
	{
	 int nfs = FontSize + Math.Sign(mwd);
	 if (nfs >= MinFontSize && nfs <= MaxFontSize) SetValue(FontSizeProperty, nfs);
	}
	else if (pointer.Properties.IsHorizontalMouseWheel)
	 HorizontalScroll.Value += mwd % 120 == 0 ? 6 * mwd / 120 * CharWidth : mwd;
	else if (e.KeyModifiers == VirtualKeyModifiers.Shift)
	 HorizontalScroll.Value -= mwd % 120 == 0 ? 3 * mwd / 120 * CharWidth : mwd;
	else
	 VerticalScroll.Value -= mwd % 120 == 0 ? 3 * mwd / 120 * CharHeight : mwd;
	IsSuggesting = false;
	e.Handled = true;
  }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new ErrorEventArgs(ex)); }
 }

 private async void VerticalScroll_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
 {
  try
  {
	if (e.NewValue == e.OldValue | VisibleLines == null | VisibleLines.Count == 0) return;
	int updown = e.NewValue > e.OldValue ? -1 : 0;
	if (Math.Abs((int)e.NewValue - (VisibleLines[0].LineNumber + updown) * CharHeight) < CharHeight) return;
	await DrawText(false, true);
  }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new ErrorEventArgs(ex)); }
 }

 private void HorizontalScroll_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
 {
  if (e.NewValue == e.OldValue) return;
  int n = Math.Max((int)(e.NewValue / CharWidth) * CharWidth, 0);
  iCharOffset = n / CharWidth;
  HorizontalOffset = -n;
  CanvasBeam.Invalidate();
  CanvasSelection.Invalidate();
  CanvasText.Invalidate();
 }

 private void VerticalScroll_Scroll(object sender, ScrollEventArgs e) { }

 private Point GetCursorPositionRelativeTo(UIElement element)
 {
  GetCursorPos(out POINT p);
  var visual = (UIElement)TextControl.XamlRoot.Content;
  var transform = element.TransformToVisual(visual);
  var origin = transform.TransformPoint(new Point(0, 0));
  return new Point(p.X - origin.X, p.Y - origin.Y);
 }

 private void TextControl_PointerExited(object sender, PointerRoutedEventArgs e)
 {
  try
  {
	var point = e.GetCurrentPoint(TextControl);
	if (point.Properties.IsLeftButtonPressed && isSelecting)
	{
	 void StartAutoScrollTimer(Func<Point, bool> shouldStop, Action<Point> updateScroll)
	 {
	  var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
	  timer.Tick += async (a, b) =>
	  {
		Point pos = GetCursorPositionRelativeTo(TextControl);
		if (shouldStop(pos) | !IsLeftMouseButtonDown()) { ((DispatcherTimer)a).Stop(); return; }
		updateScroll(pos);
		Selection = new(Selection.Start, await PointToPlace(pos));
	  };
	  timer.Start();
	 }
	 if (point.Position.Y < 0)
	  StartAutoScrollTimer(pos => pos.Y > 0, pos => VerticalScroll.Value += pos.Y);
	 else if (point.Position.Y > TextControl.ActualHeight - 2 * CharHeight)
	  StartAutoScrollTimer(pos => pos.Y < TextControl.ActualSize.Y, pos => VerticalScroll.Value += pos.Y - TextControl.ActualHeight);
	 if (point.Position.X < 0)
	  StartAutoScrollTimer(pos => pos.X > 0, pos => HorizontalScroll.Value += pos.X);
	 else if (point.Position.X >= TextControl.ActualWidth - 2 * CharWidth)
	  StartAutoScrollTimer(pos => pos.X < TextControl.ActualWidth, pos => HorizontalScroll.Value += pos.X - TextControl.ActualWidth);
	}
	e.Handled = true;
	TextControl.Focus(FocusState.Pointer);
  }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new ErrorEventArgs(ex)); }
 }

 private void TextControl_PointerLost(object sender, PointerRoutedEventArgs e)
 {
  if (isSelecting) { e.Handled = true; TextControl.Focus(FocusState.Pointer); }
  isSelecting = false;
  isLineSelect = false;
  isDragging = false;
  tempFocus = false;
 }
}
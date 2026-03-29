using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.UI;

namespace CodeEditorControl_WinUI;

public partial class CodeWriter : UserControl, INotifyPropertyChanged
{
 private static new readonly DependencyProperty FontSizeProperty = DependencyProperty.Register("FontSize", typeof(int), typeof(CodeWriter), new PropertyMetadata(12, (d, e) => ((CodeWriter)d).FontSizeChanged(d, e)));
 public static readonly DependencyProperty FontProperty = DependencyProperty.Register("Font", typeof(string), typeof(CodeWriter), new PropertyMetadata("Consolas", (d, e) => ((CodeWriter)d).FontChanged(d, e)));
 public static readonly DependencyProperty IsFoldingEnabledProperty = DependencyProperty.Register("IsFoldingEnabled", typeof(bool), typeof(CodeWriter), new PropertyMetadata(true, (d, e) => ((CodeWriter)d).DrawText(false, true)));
 public static readonly DependencyProperty HasUnsavedChangesProperty = DependencyProperty.Register("HasUnsavedChanges", typeof(bool), typeof(CodeWriter), new PropertyMetadata(true, (d, e) => ((CodeWriter)d).HasUnsavedChangesChanged(d, e)));
 public static readonly DependencyProperty IsWrappingEnabledProperty = DependencyProperty.Register("IsWrappingEnabled", typeof(bool), typeof(CodeWriter), new PropertyMetadata(true, (d, e) => ((CodeWriter)d).DrawText(false, true)));
 public static readonly DependencyProperty WrappingLengthProperty = DependencyProperty.Register("WrappingLength", typeof(int), typeof(CodeWriter), new PropertyMetadata(1, (d, e) => ((CodeWriter)d).DrawText(false, true)));
 public static new readonly DependencyProperty LanguageProperty = DependencyProperty.Register("Language", typeof(Language), typeof(CodeWriter), new PropertyMetadata(new Language("ConTeXt"), (d, e) => ((CodeWriter)d).LanguageChanged()));
 public static new readonly DependencyProperty RequestedThemeProperty = DependencyProperty.Register("RequestedTheme", typeof(ElementTheme), typeof(CodeWriter), new PropertyMetadata(12, (d, e) => ((CodeWriter)d).RequestedThemeChanged(d, e)));
 public static readonly DependencyProperty ScrollPositionProperty = DependencyProperty.Register("ScrollPosition", typeof(Place), typeof(CodeWriter), new PropertyMetadata(new Place(), (d, e) => ((CodeWriter)d).OnScrollPositionChanged(d, e)));
 public static readonly DependencyProperty ShowControlCharactersProperty = DependencyProperty.Register("ShowControlCharacters", typeof(bool), typeof(CodeWriter), new PropertyMetadata(true, (d, e) => ((CodeWriter)d).OnShowControlCharactersChanged(d, e)));
 public static readonly DependencyProperty ShowIndentGuidesProperty = DependencyProperty.Register("ShowIndentGuides", typeof(IndentGuide), typeof(CodeWriter), new PropertyMetadata(IndentGuide.None, (d, e) => ((CodeWriter)d).DrawText(false, true)));
 public static readonly DependencyProperty ShowHorizontalTicksProperty = DependencyProperty.Register("ShowHorizontalTicks", typeof(bool), typeof(CodeWriter), new PropertyMetadata(false, (d, e) => ((CodeWriter)d).DrawText(false, true)));
 public static readonly DependencyProperty ShowLineMarkersProperty = DependencyProperty.Register("ShowLineMarkers", typeof(bool), typeof(CodeWriter), new PropertyMetadata(true, (d, e) => ((CodeWriter)d).DrawText(false, true)));
 public static readonly DependencyProperty ShowLineNumbersProperty = DependencyProperty.Register("ShowLineNumbers", typeof(bool), typeof(CodeWriter), new PropertyMetadata(true, (d, e) => ((CodeWriter)d).DrawText(false, true)));
 public static readonly DependencyProperty ShowScrollbarMarkersProperty = DependencyProperty.Register("ShowScrollbarMarkers", typeof(bool), typeof(CodeWriter), new PropertyMetadata(true, (d, e) => ((CodeWriter)d).DrawText(false, true)));
 public static readonly DependencyProperty ShowScrollbarsProperty = DependencyProperty.Register("ShowScrollbars", typeof(bool), typeof(CodeWriter), new PropertyMetadata(false, (d, e) => ((CodeWriter)d).OnShowScrollbarsChanged(d, e)));
 public static readonly DependencyProperty TabLengthProperty = DependencyProperty.Register("TabLength", typeof(int), typeof(CodeWriter), new PropertyMetadata(2, (d, e) => ((CodeWriter)d).OnTabLengthChanged(d, e)));
 public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(CodeWriter), new PropertyMetadata(null, (d, e) => ((CodeWriter)d).OnTextChanged(d, e)));
 public static readonly DependencyProperty CurrentLineProperty = DependencyProperty.Register("CurrentLine", typeof(Place), typeof(CodeWriter), new PropertyMetadata(new Place(0, 0), (d, e) => ((CodeWriter)d).CurrentLineChanged(d, e)));
 public static readonly DependencyProperty EditActionHistoryProperty = DependencyProperty.Register("EditActionHistory", typeof(ObservableCollection<EditAction>), typeof(CodeWriter), new PropertyMetadata(new ObservableCollection<EditAction>(), (d, e) => ((CodeWriter)d).EditActionHistoryChanged(d, e)));

 public new int FontSize { get => Math.Max((int)GetValue(FontSizeProperty), 1); set => SetValue(FontSizeProperty, value); }
 public string Font { get => (string)GetValue(FontProperty); set => SetValue(FontProperty, value); }
 public bool IsFoldingEnabled { get => (bool)GetValue(IsFoldingEnabledProperty); set => SetValue(IsFoldingEnabledProperty, value); }
 public bool HasUnsavedChanges { get => (bool)GetValue(HasUnsavedChangesProperty); set => SetValue(HasUnsavedChangesProperty, value); }
 public bool IsWrappingEnabled { get => (bool)GetValue(IsWrappingEnabledProperty); set => SetValue(IsWrappingEnabledProperty, value); }
 public int WrappingLength { get => (int)GetValue(WrappingLengthProperty); set => SetValue(WrappingLengthProperty, value); }
 public new Language Language { get => (Language)GetValue(LanguageProperty); set => SetValue(LanguageProperty, value); }
 public new ElementTheme RequestedTheme { get => (ElementTheme)GetValue(RequestedThemeProperty); set => SetValue(RequestedThemeProperty, value); }
 public Place ScrollPosition { get => (Place)GetValue(ScrollPositionProperty); set => SetValue(ScrollPositionProperty, value); }
 public bool ShowControlCharacters { get => (bool)GetValue(ShowControlCharactersProperty); set => SetValue(ShowControlCharactersProperty, value); }
 public IndentGuide ShowIndentGuides { get => (IndentGuide)GetValue(ShowIndentGuidesProperty); set => SetValue(ShowIndentGuidesProperty, value); }
 public bool ShowHorizontalTicks { get => (bool)GetValue(ShowHorizontalTicksProperty); set => SetValue(ShowHorizontalTicksProperty, value); }
 public bool ShowLineMarkers { get => (bool)GetValue(ShowLineMarkersProperty); set => SetValue(ShowLineMarkersProperty, value); }
 public bool ShowLineNumbers { get => (bool)GetValue(ShowLineNumbersProperty); set => SetValue(ShowLineNumbersProperty, value); }
 public bool ShowScrollbarMarkers { get => (bool)GetValue(ShowScrollbarMarkersProperty); set => SetValue(ShowScrollbarMarkersProperty, value); }
 public bool ShowScrollbars { get => (bool)GetValue(ShowScrollbarsProperty); set => SetValue(ShowScrollbarsProperty, value); }
 public int TabLength { get => (int)GetValue(TabLengthProperty); set => SetValue(TabLengthProperty, value); }
 public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
 public Place CurrentLine { get => (Place)GetValue(CurrentLineProperty); set => SetValue(CurrentLineProperty, value); }

 public int CharHeight { get => Get(16); set => Set(value); }
 public int CharWidth { get => Get(8); set => Set(value); }
 public int HorizontalOffset { get => Get(0); set => Set(value); }
 public int ScaledFontSize => (int)((float)FontSize * Scale);
 public int Width_Left => Width_LeftMargin + Width_LineNumber + Width_ErrorMarker + Width_WarningMarker + Width_FoldingMarker + Width_TextIndent;
 public int Width_LineNumber { get; set; }
 public int Width_LeftMargin { get; set; }
 public bool WordWrap { get; private set; } = true;
 public int startFontsize = 16;
 public int MaxFontSize = 100;
 public int MinFontSize = 6;

 public ICommand Command_Copy { get; set; }
 public ICommand Command_Cut { get; set; }
 public ICommand Command_Delete { get; set; }
 public ICommand Command_Find { get; set; }
 public ICommand Command_Paste { get; set; }
 public ICommand Command_SelectAll { get; set; }
 public ICommand Command_Undo { get; set; }
 public ICommand Command_ToggleComment { get; set; }

 public Color Color_Beam { get => Get(Color.FromArgb(255, 200, 200, 200)); set => Set(value); }
 public Color Color_Background { get => Get(Color.FromArgb(25, 135, 135, 135)); set => Set(value); }
 public Color Color_FoldingMarker { get => Get(Color.FromArgb(255, 140, 140, 140)); set => Set(value); }
 public Color Color_FoldingMarkerUnselected { get => Get(Color.FromArgb(150, 140, 140, 140)); set => Set(value); }
 public Color Color_LeftBackground { get => Get(Color.FromArgb(10, 135, 135, 135)); set => Set(value); }
 public Color Color_LineNumber { get => Get(Color.FromArgb(255, 210, 210, 210)); set => Set(value); }
 public Color Color_LineNumberUnselected { get => Get(Color.FromArgb(160, 210, 210, 210)); set => Set(value); }
 public Color Color_SelelectedLineBackground { get => Get(Color.FromArgb(20, 210, 210, 210)); set => Set(value); }
 public Color Color_Selection { get => Get(Color.FromArgb(255, 50, 75, 100)); set => Set(value); }
 public Color Color_UnsavedMarker { get => Get(Color.FromArgb(255, 80, 190, 230)); set => Set(value); }
 public Color Color_WeakMarker { get => Get(Color.FromArgb(255, 60, 60, 60)); set => Set(value); }

 private string FontUri { get; set; } = "Consolas";
 private new float Scale => XamlRoot?.RasterizationScale is not null ? (float)XamlRoot.RasterizationScale : 1.0f;
 private int IntelliSenseWidth { get => Get(300); set => Set(value); }
 private int Width_ErrorMarker { get; set; }
 private int Width_FoldingMarker { get; set; }
 private int Width_TextIndent => CharWidth / 2;
 private int Width_WarningMarker { get; set; }

 private void HasUnsavedChangesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { }
 private void OnScrollPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { }
 private void OnShowControlCharactersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => CanvasText.Invalidate();
 private void OnTabLengthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => DrawText(false, true);

 private void FontChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
 {
  FontUri = Font switch
  {
	"Fira Code" => "CodeEditorControl-WinUI/Fonts/FiraCode.ttf#Fira Code",
	"JetBrains Mono" => "CodeEditorControl-WinUI/Fonts/JetBrainsMono.ttf#JetBrains Mono Thin",
	_ => Font,
  };
  int currline = VerticalScroll != null ? (int)VerticalScroll.Value / CharHeight : 0;
  DrawText(false, true);
  if (VerticalScroll != null) VerticalScroll.Value = currline * CharHeight;
  IntelliSenseWidth = Math.Max(150, Math.Min(20 * CharWidth, 300));
 }

 private void FontSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
 {
  int currline = VerticalScroll != null ? (int)VerticalScroll.Value / CharHeight : 0;
  DrawText(true, true);
  if (VerticalScroll != null) VerticalScroll.Value = currline * CharHeight;
  IntelliSenseWidth = Math.Max(150, Math.Min(20 * CharWidth, 300));
 }

 private void LanguageChanged()
 {
  CanToggleComment = !string.IsNullOrEmpty(Language.LineComment);
  foreach (Line line in SnapshotSafe(Lines)) line?.Language = Language;
  DispatcherQueue.TryEnqueue(() => DrawText(false, true));
 }

 private void RequestedThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
 {
  try
  {
	ActualTheme = (ElementTheme)e.NewValue;
	if ((ElementTheme)e.NewValue == ElementTheme.Default)
	{
	 var backcolor = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
	 ActualTheme = backcolor.Equals(Colors.White) ? ElementTheme.Light : ElementTheme.Dark;
	}
  }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new ErrorEventArgs(ex)); }
  DrawText(false, true);
 }

 private async void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
 {
  if ((d as CodeWriter).IsSettingValue) return;
  while (!CanvasText.IsLoaded | !CanvasBeam.IsLoaded | !CanvasSelection.IsLoaded | !CanvasScrollbarMarkers.IsLoaded)
	await Task.Delay(10);
  await InitializeLines((string)e.NewValue);
  TextChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
  TextChangedTimer.Stop();
  TextChangedTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
  TextChangedTimerLastText = Text;
  TextChangedTimer.Tick += (a, b) => { if (Text != TextChangedTimerLastText) textChanged(); };
  TextChangedTimer.Start();
 }
}
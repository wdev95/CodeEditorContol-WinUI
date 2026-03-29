using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeEditorControl_WinUI;
public class WidthToThickness : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string culture) => new Thickness(0, (double)value, 0, (double)value);
	public object ConvertBack(object value, Type targetType, object parameter, string culture) => 0;
}
public class Multiply : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string culture)
		=> Math.Max(Math.Min(double.Parse(value.ToString(), CultureInfo.InvariantCulture) * double.Parse(parameter.ToString(), CultureInfo.InvariantCulture), 32), 12);
	public object ConvertBack(object value, Type targetType, object parameter, string culture) => 0;
}
public class TokenToColor : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string culture)
		=> value is Token token ? EditorOptions.TokenColors[token] : EditorOptions.TokenColors[Token.Normal];
	public object ConvertBack(object value, Type targetType, object parameter, string culture) => 0;
}
public class FocusToVisibility : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string culture)
		=> (FocusState)value != FocusState.Unfocused ? Visibility.Visible : Visibility.Collapsed;
	public object ConvertBack(object value, Type targetType, object parameter, string culture) => 0;
}
public class ArgumentsToString : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string culture)
	{
		string result = "";
		foreach (var item in (List<Argument>)value)
		{
			var (s, e) = item.Delimiters switch
			{
				"parentheses " => ("(", ")"),
				"braces" => ("{", "}"),
				"anglebrackets" => ("<", ">"),
				"none" => ("", ""),
				_ => ("[", "]"),
			};
			result += $" {s}...{e}";
		}
		return result;
	}
	public object ConvertBack(object value, Type targetType, object parameter, string culture) => null;
}
public class SuggestionTemplateSelector : DataTemplateSelector
{
	public DataTemplate IntelliSenseTemplate { get; set; }
	public DataTemplate ArgumentTemplate { get; set; }
	protected override DataTemplate SelectTemplateCore(object item, DependencyObject dependency)
		=> item is IntelliSense ? IntelliSenseTemplate : item is Parameter ? ArgumentTemplate : null;
}

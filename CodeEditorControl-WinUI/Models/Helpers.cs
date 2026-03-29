using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using Windows.Foundation;
using Windows.UI;

namespace CodeEditorControl_WinUI;
/// <summary>Base class providing dictionary-backed observable properties.</summary>
public class Bindable : INotifyPropertyChanged
{
	private readonly Dictionary<string, object> _properties = new();
	public event PropertyChangedEventHandler PropertyChanged;
	protected T Get<T>(T defaultVal = default, [CallerMemberName] string name = null)
	{
		if (!_properties.TryGetValue(name, out object value))
			value = _properties[name] = defaultVal;
		return (T)value;
	}
	protected void Set<T>(T value, [CallerMemberName] string name = null)
	{
		if (Equals(value, Get<T>(value, name))) return;
		_properties[name] = value;
		OnPropertyChanged(name);
	}
	protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
		=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
public static class LogHelper
{
	[System.Diagnostics.Conditional("DEBUG")]
	public static void CWLog(string message)
	{
		try { System.Diagnostics.Debug.WriteLine($"[CodeWriter] {message}"); } catch { }
	}
}
public static class Extensions
{
	public static Vector2 Center(this Rect rect) => new((float)rect.X + (float)rect.Width / 2, (float)rect.Y + (float)rect.Height / 2);
	public static Color ChangeColorBrightness(this Color color, float correctionFactor)
	{
		float red = color.R, green = color.G, blue = color.B;
		if (correctionFactor < 0)
		{
			correctionFactor = 1 + correctionFactor;
			red *= correctionFactor; green *= correctionFactor; blue *= correctionFactor;
		}
		else
		{
			red = (255 - red) * correctionFactor + red;
			green = (255 - green) * correctionFactor + green;
			blue = (255 - blue) * correctionFactor + blue;
		}
		return Color.FromArgb(color.A, (byte)red, (byte)green, (byte)blue);
	}
	public static Color InvertColorBrightness(this Color color)
	{
		float red = color.R, green = color.G, blue = color.B;
		float lumi = 0.33f * red + 0.33f * green + 0.33f * blue;
		red = 255 - lumi + 0.6f * (red - lumi);
		green = 255 - lumi + 0.35f * (green - lumi);
		blue = 255 - lumi + 0.4f * (blue - lumi);
		return Color.FromArgb(color.A, (byte)red, (byte)green, (byte)blue);
	}
	public static System.Drawing.Point ToDrawingPoint(this Point point) => new((int)point.X, (int)point.Y);
	public static Point ToFoundationPoint(this System.Drawing.Point point) => new(point.X, point.Y);
	public static Color ToUIColor(this System.Drawing.Color color) => Color.FromArgb(color.A, color.R, color.G, color.B);
	public static Vector2 ToVector2(this System.Drawing.Point point) => new(point.X, point.Y);
}

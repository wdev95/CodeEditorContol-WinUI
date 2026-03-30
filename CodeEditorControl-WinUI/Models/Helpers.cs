using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Windows.Foundation;
using Windows.UI;

namespace CodeEditorControl_WinUI;
/// <summary>Base class providing dictionary-backed observable properties.</summary>
public class Bindable : INotifyPropertyChanged
{
 private Dictionary<string, object> _properties = new Dictionary<string, object>();

 public event PropertyChangedEventHandler PropertyChanged;

 protected T Get<T>(T defaultVal = default, [CallerMemberName] string name = null)
 {
  if (!_properties.TryGetValue(name, out object value))
  {
	value = _properties[name] = defaultVal;
  }
  return (T)value;
 }

 protected void Set<T>(T value, [CallerMemberName] string name = null)
 {
  //if (name != "Blocks")
  if (Equals(value, Get<T>(value, name)))
	return;
  _properties[name] = value;

  //if (name != "FileContent")
  OnPropertyChanged(name);
 }

 protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
 {
  var handlers = PropertyChanged;
  if (handlers == null)
	return;

  void RaiseHandlers()
  {
	var args = new PropertyChangedEventArgs(propertyName);
	foreach (PropertyChangedEventHandler handler in handlers.GetInvocationList().OfType<PropertyChangedEventHandler>())
	{
	 try
	 {
	  handler(this, args);
	 }
	 catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException || ex is InvalidOperationException)
	 {
	 }
	}
  }

  try
  {
	var queue = DispatcherQueue.GetForCurrentThread();
	if (queue != null && !queue.HasThreadAccess)
	{
	 if (!queue.TryEnqueue(RaiseHandlers))
	  RaiseHandlers();
	}
	else
	{
	 RaiseHandlers();
	}
  }
  catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException || ex is InvalidOperationException)
  {
  }
 }
}

public interface IDependable { }

public static class Dependable
{
 private static readonly ConcurrentDictionary<(Type OwnerType, string PropertyName), DependencyProperty> RegisteredProperties = new();
 private static readonly ConcurrentDictionary<(Type OwnerType, string PropertyName), Action<DependencyObject, DependencyPropertyChangedEventArgs>> RegisteredCallbacks = new();
 private static readonly ConcurrentDictionary<(Type OwnerType, string PropertyName), Action<object, DependencyPropertyChangedEventArgs>> ResolvedMethodCallbacks = new();

 public static T GetDp<T>(this IDependable owner, T defaultVal = default, [CallerMemberName] string name = null)
 {
  if (owner == null)
	throw new ArgumentNullException(nameof(owner));
  if (owner is not DependencyObject dependencyObject)
	throw new InvalidOperationException($"{owner.GetType().Name} must inherit from DependencyObject to use Dependable helpers.");

  var dp = EnsureProperty<T>(dependencyObject.GetType(), name);
  object value = dependencyObject.GetValue(dp);

  if (dependencyObject.ReadLocalValue(dp) == DependencyProperty.UnsetValue)
  {
	dependencyObject.SetValue(dp, defaultVal);
	value = defaultVal;
  }

  return value is T typed ? typed : defaultVal;
 }

 public static void SetDp<T>(this IDependable owner, T value, [CallerMemberName] string name = null)
 {
  if (owner == null)
	throw new ArgumentNullException(nameof(owner));
  if (owner is not DependencyObject dependencyObject)
	throw new InvalidOperationException($"{owner.GetType().Name} must inherit from DependencyObject to use Dependable helpers.");

  var dp = EnsureProperty<T>(dependencyObject.GetType(), name);
  dependencyObject.SetValue(dp, value);
 }

 public static void RegisterDpChangeCallback(this IDependable owner, string propertyName, Action<DependencyObject, DependencyPropertyChangedEventArgs> callback)
 {
  if (owner == null)
	throw new ArgumentNullException(nameof(owner));
  if (owner is not DependencyObject dependencyObject)
	throw new InvalidOperationException($"{owner.GetType().Name} must inherit from DependencyObject to use Dependable helpers.");
  if (string.IsNullOrWhiteSpace(propertyName))
	throw new ArgumentException("Property name is required.", nameof(propertyName));

  if (callback != null)
	RegisteredCallbacks[(dependencyObject.GetType(), propertyName)] = callback;
 }

 public static T Get<T>(this DependencyObject owner, T defaultVal = default, [CallerMemberName] string name = null)
 {
  if (owner == null)
	throw new ArgumentNullException(nameof(owner));

  EnsureCompatible(owner);
  var dp = EnsureProperty<T>(owner.GetType(), name);
  object value = owner.GetValue(dp);

  if (owner.ReadLocalValue(dp) == DependencyProperty.UnsetValue)
  {
	owner.SetValue(dp, defaultVal);
	value = defaultVal;
  }

  return value is T typed ? typed : defaultVal;
 }

 public static void Set<T>(this DependencyObject owner, T value, [CallerMemberName] string name = null)
 {
  if (owner == null)
	throw new ArgumentNullException(nameof(owner));

  EnsureCompatible(owner);
  var dp = EnsureProperty<T>(owner.GetType(), name);
  owner.SetValue(dp, value);
 }

 public static void RegisterChangeCallback(this DependencyObject owner, string propertyName, Action<DependencyObject, DependencyPropertyChangedEventArgs> callback)
 {
  if (owner == null)
	throw new ArgumentNullException(nameof(owner));
  if (string.IsNullOrWhiteSpace(propertyName))
	throw new ArgumentException("Property name is required.", nameof(propertyName));

  EnsureCompatible(owner);
  if (callback != null)
	RegisteredCallbacks[(owner.GetType(), propertyName)] = callback;
 }

 private static void EnsureCompatible(DependencyObject owner)
 {
  if (owner is not IDependable)
	throw new InvalidOperationException($"{owner.GetType().Name} must implement IDependable to use Dependable helpers.");
 }

 private static DependencyProperty EnsureProperty<T>(Type ownerType, string propertyName)
 {
  var key = (ownerType, propertyName);
  return RegisteredProperties.GetOrAdd(key, _ => DependencyProperty.Register(
	propertyName,
	typeof(T),
	ownerType,
	new PropertyMetadata(default(T), (d, e) =>
	{
	 if (RegisteredCallbacks.TryGetValue((d.GetType(), propertyName), out var callback) && callback != null)
	  callback(d, e);

	 var resolved = ResolvedMethodCallbacks.GetOrAdd((d.GetType(), propertyName), static methodKey => ResolveMethodCallback(methodKey.OwnerType, methodKey.PropertyName));
	 resolved?.Invoke(d, e);
	})));
 }

 private static Action<object, DependencyPropertyChangedEventArgs> ResolveMethodCallback(Type ownerType, string propertyName)
 {
  const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
  MethodInfo method = ownerType.GetMethod(propertyName + "Changed", flags)
	?? ownerType.GetMethod("On" + propertyName + "Changed", flags);

  if (method == null)
	return null;

  var parameters = method.GetParameters();
  if (parameters.Length == 2
	&& typeof(DependencyObject).IsAssignableFrom(parameters[0].ParameterType)
	&& typeof(DependencyPropertyChangedEventArgs).IsAssignableFrom(parameters[1].ParameterType))
	return (instance, args) => method.Invoke(instance, new object[] { instance, args });

  if (parameters.Length == 0)
	return (instance, args) => method.Invoke(instance, Array.Empty<object>());

  return null;
 }
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

using Skua.Core.Interfaces;
using System;
using System.Xml.Linq;

namespace Skua.App.Avalonia.Flash;

#if IS_WINDOWS
using AxShockwaveFlashObjects;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Flash;
using Skua.Core.Messaging;
using Skua.Core.Utils;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using global::Avalonia.Threading;

public class FlashUtil : IFlashUtil
{
    public FlashUtil(IMessenger messenger, Lazy<IScriptManager> manager)
    {
        _messenger = messenger;
        _lazyManager = manager;
    }

    private readonly IMessenger _messenger;
    private readonly Lazy<IScriptManager> _lazyManager;
    private AxShockwaveFlash? Flash;

    public event FlashCallHandler? FlashCall;

    private static byte[]? _cachedSwf;

    public static void PreloadSwf()
    {
        try
        {
            if (_cachedSwf != null)
                return;

            string swfPath = Path.Combine(AppContext.BaseDirectory, "skua.swf");
            if (File.Exists(swfPath))
                _cachedSwf = File.ReadAllBytes(swfPath);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to preload skua.swf: {ex}");
        }
    }

    public void InitializeFlash()
    {
        string swfPath = Path.Combine(AppContext.BaseDirectory, "skua.swf");
        if (!File.Exists(swfPath))
        {
            MessageBox.Show(
                "Missing skua.swf. Build the AS3 client first so Skua.App.Avalonia can load the game content.",
                "Missing game content",
                MessageBoxButtons.OK,
                MessageBoxIcon.Exclamation);
            Environment.Exit(0);
            return;
        }

        try
        {
            if (!EoLHook.IsHooked)
                EoLHook.Hook();

            CleanupOldFlash();

            AxShockwaveFlash flash = new();
            flash.BeginInit();
            flash.Name = "flash";
            flash.Dock = DockStyle.Fill;
            flash.TabIndex = 0;
            flash.FlashCall += CallHandler;

            _messenger.Send<FlashChangedMessage<AxShockwaveFlash>>(new(flash));
            flash.EndInit();
            flash.WMode = "window";
            flash.ScaleMode = 0;
            flash.CtlScale = "ShowAll";
            Flash = flash;
            byte[] swf = _cachedSwf ?? File.ReadAllBytes(swfPath);
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);
            writer.Write(8 + swf.Length);
            writer.Write(1432769894);
            writer.Write(swf.Length);
            writer.Write(swf);
            writer.Seek(0, SeekOrigin.Begin);
            flash.OcxState = new AxHost.State(stream, 1, false, null);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Flash initialization error: {ex}");
            if (MessageBox.Show($"Please, uninstall and then reinstall CleanFlash via the Gif provided \r\nDo you want view the instructional Gif?", "Clean Flash missing/installed incorrectly", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
                Ioc.Default.GetRequiredService<IProcessService>().OpenLink("https://imgur.com/ztsLYZ1");
            Environment.Exit(0);
        }
        finally
        {
            EoLHook.Unhook();
        }
    }

    private void CleanupOldFlash()
    {
        if (Flash == null) return;

        Flash.FlashCall -= CallHandler;

        try
        {
            Flash.Stop();
        }
        catch { /* ignored */ }

        object? ocx = null;
        try
        {
            ocx = Flash.GetOcx();
        }
        catch { /* ignored */ }

        Flash.Dispose();
        Flash = null;

        if (ocx != null)
        {
            try
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(ocx);
            }
            catch { /* ignored */ }
        }
    }

    private void CallHandler(object sender, _IShockwaveFlashEvents_FlashCallEvent e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(e?.request))
                return;

            XElement el = XElement.Parse(e.request);
            string? function = el.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(function))
                return;

            object[] args = el.Elements().Select(x => FromFlashXml(x)).ToArray();
            FlashCall?.Invoke(function, args);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Flash CallHandler exception: {ex.Message}");
        }
    }

    public string Call(string function, params object[] args)
    {
        return Call<string>(function, args);
    }

    public T Call<T>(string function, params object[] args)
    {
        try
        {
            object? o = Call(function, typeof(T), args);
            return o is T value ? value : GetDefaultValue<T>();
        }
        catch
        {
            return GetDefaultValue<T>();
        }
    }

    private static T GetDefaultValue<T>()
    {
        object? defaultValue = DefaultProvider.GetDefault<T>(typeof(T));
        return defaultValue is T typed ? typed : default!;
    }

    public object Call(string function, Type type, params object[] args)
    {
        if (_lazyManager.Value.ShouldExit && Thread.CurrentThread.Name == "Script Thread")
            _lazyManager.Value.ScriptCts?.Token.ThrowIfCancellationRequested();

        try
        {
            if (Flash is null)
                return DefaultProvider.GetDefault(type)!;

            StringBuilder req = new StringBuilder().Append($"<invoke name=\"{function}\" returntype=\"xml\">");
            if (args.Length > 0)
            {
                req.Append("<arguments>");
                args.ForEach(o => req.Append(ToFlashXml(o)));
                req.Append("</arguments>");
            }
            req.Append("</invoke>");

            string? result = null;
            const int maxRetries = 2;
            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    Stopwatch callTimer = Stopwatch.StartNew();
                    if (Dispatcher.UIThread.CheckAccess())
                    {
                        result = Flash.CallFunction(req.ToString());
                    }
                    else
                    {
                        result = Dispatcher.UIThread.Invoke(() => Flash?.CallFunction(req.ToString()));
                    }
                    callTimer.Stop();

                    if (callTimer.ElapsedMilliseconds >= 250)
                        Trace.WriteLine($"Slow Flash call: {function} took {callTimer.ElapsedMilliseconds} ms (attempt {attempt + 1}/{maxRetries + 1}).");

                    break;
                }
                catch (COMException comEx) when (attempt < maxRetries && IsTransientComError(comEx.HResult))
                {
                    Thread.Sleep(15 * (attempt + 1));
                }
                catch (Exception ex) when (attempt < maxRetries && IsCrossThreadOrComError(ex))
                {
                    Thread.Sleep(15 * (attempt + 1));
                }
            }

            if (string.IsNullOrWhiteSpace(result))
                return DefaultProvider.GetDefault(type)!;

            XElement el = XElement.Parse(result);
            object? rawValue = FromFlashXml(el);
            return ConvertParsedValue(rawValue, type) ?? DefaultProvider.GetDefault(type)!;
        }
        catch (Exception e)
        {
            if (function != "isKicked")
                _messenger.Send<FlashErrorMessage>(new(e, function, args));
            return DefaultProvider.GetDefault(type)!;
        }
    }

    private static object? ConvertParsedValue(object? rawValue, Type type)
    {
        if (rawValue is null)
            return DefaultProvider.GetDefault(type);

        if (type.IsInstanceOfType(rawValue))
            return rawValue;

        if (type == typeof(bool))
        {
            if (rawValue is bool b) return b;
            string s = rawValue.ToString()?.Trim() ?? string.Empty;
            if (bool.TryParse(s, out bool parsedBool)) return parsedBool;
            if (int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out int intVal)) return intVal != 0;
            return s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";
        }

        if (type == typeof(int))
        {
            if (rawValue is int i) return i;
            if (rawValue is IConvertible)
            {
                try { return Convert.ToInt32(rawValue, CultureInfo.InvariantCulture); } catch { }
            }
            string s = rawValue.ToString()?.Trim() ?? string.Empty;
            if (int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out int parsedInt)) return parsedInt;
            return 0;
        }

        if (type == typeof(float))
        {
            if (rawValue is float f) return f;
            if (rawValue is IConvertible)
            {
                try { return Convert.ToSingle(rawValue, CultureInfo.InvariantCulture); } catch { }
            }
            string s = rawValue.ToString()?.Trim() ?? string.Empty;
            if (float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out float parsedFloat)) return parsedFloat;
            return 0f;
        }

        if (type == typeof(double))
        {
            if (rawValue is double d) return d;
            if (rawValue is IConvertible)
            {
                try { return Convert.ToDouble(rawValue, CultureInfo.InvariantCulture); } catch { }
            }
            string s = rawValue.ToString()?.Trim() ?? string.Empty;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedDouble)) return parsedDouble;
            return 0d;
        }

        if (type == typeof(string))
        {
            return rawValue.ToString() ?? string.Empty;
        }

        try
        {
            return Convert.ChangeType(rawValue, type, CultureInfo.InvariantCulture);
        }
        catch
        {
            return DefaultProvider.GetDefault(type);
        }
    }

    private static bool IsCrossThreadOrComError(Exception ex)
    {
        if (ex is InvalidOperationException)
            return true;
        if (ex is COMException comEx && IsTransientComError(comEx.HResult))
            return true;
        if (ex.GetType().Name == "InvalidActiveXStateException")
            return true;
        return false;
    }

    private static bool IsTransientComError(int hresult)
    {
        return hresult == unchecked((int)0x8001010A)  // RPC_E_SERVERCALL_RETRYLATER
            || hresult == unchecked((int)0x80010001)  // RPC_E_CALL_REJECTED
            || hresult == unchecked((int)0x8001010E)  // RPC_E_WRONG_THREAD
            || hresult == unchecked((int)0x80004005)  // E_FAIL
            || hresult == unchecked((int)0x800706BE); // RPC_S_CALL_FAILED
    }

    public static string ToFlashXml(object o)
    {
        switch (o)
        {
            case null:
                return "<null/>";

            case bool _:
                return $"<{o.ToString()!.ToLower()}/>";

            case double _:
            case float _:
            case long _:
            case int _:
                return $"<number>{o}</number>";

            case ExpandoObject _:
                StringBuilder sb = new StringBuilder().Append("<object>");
                foreach (KeyValuePair<string, object> kvp in (o as IDictionary<string, object>)!)
                    sb.Append($"<property id=\"{kvp.Key}\">{ToFlashXml(kvp.Value)}</property>");
                return sb.Append("</object>").ToString();

            default:
                if (o is Array)
                {
                    StringBuilder _sb = new StringBuilder().Append("<array>");
                    int k = 0;
                    foreach (object el in (o as Array)!)
                        _sb.Append($"<property id=\"{k++}\">{ToFlashXml(el)}</property>");
                    return _sb.Append("</array>").ToString();
                }
                return $"<string>{SecurityElement.Escape(o.ToString())}</string>";
        }
    }

    public object FromFlashXml(XElement el)
    {
        switch (el.Name.LocalName)
        {
            case "number":
                return int.TryParse(el.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out int i)
                    ? i
                    : float.TryParse(el.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out float f)
                        ? f
                        : 0;

            case "true":
                return true;

            case "false":
                return false;

            case "null":
            case "undefined":
                return null!;

            case "array":
                return el.Elements().Select(e => FromFlashXml(e)).ToArray();

            case "object":
                dynamic d = new ExpandoObject();
                el.Elements().ForEach(e => d[e.Attribute("id")!.Value] = FromFlashXml(e.Elements().First()));
                return d;

            case "string":
                return el.Value;

            default:
                return el.Value;
        }
    }

    public IFlashObject<T> CreateFlashObject<T>(string path)
    {
        return new FlashObject<T>(Call<int>("lnkCreate", path), this);
    }

    public void Dispose()
    {
        try
        {
            CleanupOldFlash();
        }
        catch { /* ignored */ }
        finally
        {
            EoLHook.Unhook();
        }
    }
}
#else
public class FlashUtil : IFlashUtil
{
    public static void PreloadSwf()
    {
    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }

    public event FlashCallHandler? FlashCall;
    public void InitializeFlash()
    {
        throw new NotImplementedException();
    }

    public string? Call(string function, params object[] args)
    {
        throw new NotImplementedException();
    }

    public T? Call<T>(string function, params object[] args)
    {
        throw new NotImplementedException();
    }

    public object? Call(string function, Type type, params object[] args)
    {
        throw new NotImplementedException();
    }

    public object FromFlashXml(XElement el)
    {
        throw new NotImplementedException();
    }

    public IFlashObject<T> CreateFlashObject<T>(string path)
    {
        throw new NotImplementedException();
    }
}
#endif

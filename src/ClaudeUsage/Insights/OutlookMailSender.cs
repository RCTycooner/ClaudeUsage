using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ClaudeUsage.Insights;

/// <summary>Sends one mail with an attachment.</summary>
public interface IMailSender
{
    /// <summary>Throws <see cref="InsightsException"/> when the mail could not be handed over.</summary>
    Task SendAsync(string recipient, string subject, string body, string attachmentPath);
}

/// <summary>
/// Sends through the installed classic Outlook (COM automation), from its default account; no SMTP settings needed.
/// Runs on its own STA thread so a slow Outlook start never blocks the UI. "New Outlook" has no COM interface.
/// </summary>
/// <remarks>
/// Late binding goes through <see cref="Type.InvokeMember(string, BindingFlags, Binder, object, object[])"/> (IDispatch,
/// in CoreLib) rather than <c>dynamic</c>: <c>dynamic</c> loads Microsoft.CSharp.dll on first use, and in a widget that
/// runs for days the shared runtime it started from can be serviced away underneath it, so that load fails.
/// </remarks>
public sealed class OutlookMailSender : IMailSender
{
    private const string ProgId = "Outlook.Application";
    private const int OlMailItem = 0;

    public Task SendAsync(string recipient, string subject, string body, string attachmentPath)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            // Anything escaping this thread would terminate the process, so every failure goes to the task.
            try
            {
                Send(recipient, subject, body, attachmentPath);
                done.SetResult();
            }
            catch (InsightsException ex)
            {
                done.SetException(ex);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                done.SetException(new InsightsException(
                    $"Outlook could not send the mail: {ex.InnerException.Message}", ex.InnerException));
            }
            catch (Exception ex)
            {
                done.SetException(new InsightsException($"Outlook could not send the mail: {ex.Message}", ex));
            }
        })
        {
            IsBackground = true,
            Name = "Outlook mail",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    // Not inlined, so a failure while compiling or loading what it needs is thrown inside the thread's try.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Send(string recipient, string subject, string body, string attachmentPath)
    {
        var type = Type.GetTypeFromProgID(ProgId)
            ?? throw new InsightsException("Classic Outlook is not installed (no Outlook.Application COM class).");

        object? outlook = null, session = null, mail = null, attachments = null, attachment = null;
        try
        {
            outlook = Activator.CreateInstance(type)!;
            session = Call(outlook, "GetNamespace", "MAPI");
            Call(session, "Logon");
            mail = Call(outlook, "CreateItem", OlMailItem);
            Set(mail, "To", recipient);
            Set(mail, "Subject", subject);
            Set(mail, "Body", body);
            attachments = Get(mail, "Attachments");
            attachment = Call(attachments, "Add", attachmentPath);
            Call(mail, "Send");

            // An Outlook started just for this can close before its outbox is sent; ask it to send now.
            Call(session, "SendAndReceive", false);
        }
        finally
        {
            Release(attachment);
            Release(attachments);
            Release(mail);
            Release(session);
            Release(outlook);
        }
    }

    private static object? Call(object? target, string method, params object?[] args) =>
        Invoke(target, method, BindingFlags.InvokeMethod, args);

    private static object? Get(object? target, string property) =>
        Invoke(target, property, BindingFlags.GetProperty, []);

    private static void Set(object? target, string property, object? value) =>
        Invoke(target, property, BindingFlags.SetProperty, [value]);

    private static object? Invoke(object? target, string name, BindingFlags flags, object?[] args)
    {
        if (target is null)
        {
            throw new InsightsException($"Outlook returned nothing before '{name}'.");
        }

        return target.GetType().InvokeMember(name, flags, binder: null, target, args, CultureInfo.InvariantCulture);
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }
}

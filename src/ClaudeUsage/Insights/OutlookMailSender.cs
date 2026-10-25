using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

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
public sealed class OutlookMailSender : IMailSender
{
    private const string ProgId = "Outlook.Application";
    private const int OlMailItem = 0;

    public Task SendAsync(string recipient, string subject, string body, string attachmentPath)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Send(recipient, subject, body, attachmentPath);
                done.SetResult();
            }
            catch (Exception ex) when (ex is COMException or RuntimeBinderException or InvalidCastException
                or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
            {
                done.SetException(new InsightsException($"Outlook could not send the mail: {ex.Message}", ex));
            }
            catch (InsightsException ex)
            {
                done.SetException(ex);
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

    private static void Send(string recipient, string subject, string body, string attachmentPath)
    {
        var type = Type.GetTypeFromProgID(ProgId)
            ?? throw new InsightsException("Classic Outlook is not installed (no Outlook.Application COM class).");

        dynamic? outlook = null, session = null, mail = null, attachments = null;
        try
        {
            outlook = Activator.CreateInstance(type)!;
            session = outlook.GetNamespace("MAPI");
            session.Logon();
            mail = outlook.CreateItem(OlMailItem);
            mail.To = recipient;
            mail.Subject = subject;
            mail.Body = body;
            attachments = mail.Attachments;
            attachments.Add(attachmentPath);
            mail.Send();

            // An Outlook started just for this can close before its outbox is sent; ask it to send now.
            session.SendAndReceive(false);
        }
        finally
        {
            Release(attachments);
            Release(mail);
            Release(session);
            Release(outlook);
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }
}

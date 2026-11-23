using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace League_Data_Recorder;
internal sealed class ShareLinkActivation : IDisposable
{
    private readonly Mutex mutex;
    private readonly CancellationTokenSource cancellation = new();
    private readonly string pipeName;
    internal bool IsPrimary { get; }
    internal ShareLinkActivation()
    {
        string user = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..16];
        pipeName = "LeagueRecorderSharing-" + user;
        mutex = new Mutex(true, "Local\\" + pipeName, out bool created); IsPrimary = created;
    }
    internal static string? GetLink(string[] args) => args.FirstOrDefault(a => a.StartsWith("leaguerecorder://", StringComparison.OrdinalIgnoreCase));
    internal void Forward(string[] args)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
        pipe.Connect(5000);
        using var writer = new BinaryWriter(pipe, Encoding.UTF8, true);
        byte[] bytes = Encoding.UTF8.GetBytes(GetLink(args) ?? "");
        if (bytes.Length > 8192) throw new InvalidDataException("Share link is too long.");
        writer.Write(bytes.Length); writer.Write(bytes); writer.Flush();
    }
    internal void Listen(Form1 form) => _ = ListenAsync(form);
    private async Task ListenAsync(Form1 form)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token); timeout.CancelAfter(5000);
                byte[] header = new byte[4]; await pipe.ReadExactlyAsync(header, timeout.Token);
                int length = BitConverter.ToInt32(header);
                if (length < 0 || length > 8192) continue;
                byte[] bytes = new byte[length]; await pipe.ReadExactlyAsync(bytes, timeout.Token);
                if (length > 0) form.ReceiveShareLink(Encoding.UTF8.GetString(bytes));
                else if (form.IsHandleCreated) form.BeginInvoke(new Action(() => { form.Show(); form.Activate(); }));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
        }
    }
    internal static void Register()
    {
        using var key = Registry.CurrentUser.CreateSubKey("Software\\Classes\\leaguerecorder");
        key.SetValue("", "URL:League Recorder Game Sharing"); key.SetValue("URL Protocol", "");
        using var command = key.CreateSubKey("shell\\open\\command");
        command.SetValue("", "\"" + Application.ExecutablePath + "\" --share \"%1\"");
    }
    public void Dispose() { cancellation.Cancel(); if (IsPrimary) mutex.ReleaseMutex(); mutex.Dispose(); }
}

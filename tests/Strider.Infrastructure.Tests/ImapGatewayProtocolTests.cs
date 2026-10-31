using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Strider.Core.Abstractions;
using Strider.Core.Domain;
using Strider.Infrastructure.Mail;

namespace Strider.Infrastructure.Tests;

/// <summary>
/// Runs the real MailKit gateway against a scripted IMAP server on a local socket.
/// The other mail tests fake the gateway itself, so they cannot notice a command the
/// gateway gets wrong; a broken snapshot would stop every sync.
/// </summary>
public class ImapGatewayProtocolTests : IAsyncDisposable
{
    private readonly ScriptedImapServer _server = new();
    private readonly MailKitImapGateway _gateway;
    private readonly Account _account;

    public ImapGatewayProtocolTests()
    {
        _account = new Account
        {
            Email = "me@mine.com",
            ImapHost = "127.0.0.1",
            ImapPort = _server.Port,
            ImapUseSsl = false,
        };
        _gateway = new MailKitImapGateway(new OneSecretKeychain(), null!, _account.Id);
    }

    public async ValueTask DisposeAsync()
    {
        _gateway.Dispose();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task Snapshot_ReturnsUidValidityAndFlags_AndIgnoresTheStarQuirk()
    {
        await _gateway.ConnectAsync(_account);

        var snapshot = await _gateway.GetFolderSnapshotAsync(_account, "INBOX", 5);

        snapshot.UidValidity.Should().Be(77);
        // The server also returns UID 2: "5:*" always includes the last message on some servers.
        snapshot.Messages.Should().Equal(
            new RemoteMessageState(5, IsRead: false, IsStarred: true),
            new RemoteMessageState(8, IsRead: true, IsStarred: false));
        _server.Commands.Should().Contain(c => c.Contains("UID FETCH 5:*") && c.Contains("FLAGS"));
        _server.Commands.Should().Contain(c => c.StartsWith("EXAMINE"), "a snapshot must not touch \\Seen or \\Recent");
    }

    [Fact]
    public async Task Folders_SkipUnselectableContainers_AndUseSpecialUseRoles()
    {
        await _gateway.ConnectAsync(_account);

        var folders = await _gateway.GetFoldersAsync(_account);

        folders.Select(f => f.RemoteName).Should().NotContain("[Gmail]", "opening a \\Noselect folder is an error");
        folders.Should().Contain(f => f.RemoteName == "INBOX" && f.Type == FolderType.Inbox);
        folders.Should().Contain(f => f.RemoteName == "[Gmail]/Bin" && f.Type == FolderType.Trash,
            "the attribute says Trash even though the name is unknown");
        folders.Should().Contain(f => f.RemoteName == "Отправленные" && f.Type == FolderType.Sent);
    }

    [Fact]
    public async Task Delete_ExpungesOnlyThatMessage()
    {
        await _gateway.ConnectAsync(_account);

        await _gateway.DeleteMessageAsync(_account, "INBOX", 8);

        _server.Commands.Should().Contain(c => c.Contains("UID STORE 8") && c.Contains("\\Deleted"));
        _server.Commands.Should().Contain("UID EXPUNGE 8");
        _server.Commands.Should().NotContain("EXPUNGE", "a plain EXPUNGE destroys what other clients marked deleted");
    }

    [Fact]
    public async Task ReadAndStar_SendTheRightFlags()
    {
        await _gateway.ConnectAsync(_account);

        await _gateway.MarkAsUnreadAsync(_account, "INBOX", 5);
        await _gateway.AddFlagAsync(_account, "INBOX", 5, "flagged");

        _server.Commands.Should().Contain(c => c.Contains("UID STORE 5") && c.Contains("-FLAGS") && c.Contains("\\Seen"));
        _server.Commands.Should().Contain(c => c.Contains("UID STORE 5") && c.Contains("+FLAGS") && c.Contains("\\Flagged"));
    }

    private sealed class OneSecretKeychain : IKeychainService
    {
        public Task<string?> GetSecretAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>("secret");
        public Task SetSecretAsync(string key, string value, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteSecretAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> HasSecretAsync(string key, CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>Answers exactly what the gateway needs; records every command without its tag.</summary>
    private sealed class ScriptedImapServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        public System.Collections.Concurrent.ConcurrentQueue<string> Commands { get; } = new();
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public ScriptedImapServer()
        {
            _listener.Start();
            _loop = Task.Run(RunAsync);
        }

        private async Task RunAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);
                await using var writer = new StreamWriter(stream, new ASCIIEncoding()) { NewLine = "\r\n", AutoFlush = true };

                await writer.WriteLineAsync("* OK ready");
                while (await reader.ReadLineAsync(_stop.Token) is { } line)
                {
                    var space = line.IndexOf(' ');
                    var tag = line[..space];
                    var command = line[(space + 1)..];
                    Commands.Enqueue(command);
                    await AnswerAsync(writer, tag, command);
                }
            }
            catch (OperationCanceledException)
            {
                // Test finished.
            }
            catch (IOException)
            {
                // Client went away.
            }
        }

        private static async Task AnswerAsync(StreamWriter w, string tag, string command)
        {
            if (command.StartsWith("CAPABILITY"))
            {
                await w.WriteLineAsync("* CAPABILITY IMAP4rev1 UIDPLUS NAMESPACE");
            }
            else if (command.StartsWith("NAMESPACE"))
            {
                await w.WriteLineAsync("* NAMESPACE ((\"\" \"/\")) NIL NIL");
            }
            else if (command.StartsWith("LIST"))
            {
                await AnswerListAsync(w, command);
            }
            else if (command.StartsWith("EXAMINE") || command.StartsWith("SELECT"))
            {
                await w.WriteLineAsync("* 3 EXISTS");
                await w.WriteLineAsync("* 0 RECENT");
                await w.WriteLineAsync("* FLAGS (\\Seen \\Flagged \\Deleted)");
                await w.WriteLineAsync("* OK [UIDVALIDITY 77] ok");
                await w.WriteLineAsync("* OK [UIDNEXT 9] ok");
                await w.WriteLineAsync($"{tag} OK [{(command.StartsWith("EXAMINE") ? "READ-ONLY" : "READ-WRITE")}] done");
                return;
            }
            else if (command.StartsWith("UID FETCH"))
            {
                await w.WriteLineAsync("* 1 FETCH (UID 2 FLAGS (\\Seen))");
                await w.WriteLineAsync("* 2 FETCH (UID 5 FLAGS (\\Flagged))");
                await w.WriteLineAsync("* 3 FETCH (UID 8 FLAGS (\\Seen))");
            }
            else if (command.StartsWith("LOGOUT"))
            {
                await w.WriteLineAsync("* BYE");
            }

            await w.WriteLineAsync($"{tag} OK done");
        }

        private static async Task AnswerListAsync(StreamWriter w, string command)
        {
            // "Отправленные" in modified UTF-7, as a server sends it.
            const string sent = "&BB4EQgQ,BEAEMAQyBDsENQQ9BD0ESwQ1-";

            if (command.Contains("\"[Gmail]/%\""))
            {
                await w.WriteLineAsync("* LIST (\\HasNoChildren \\Trash) \"/\" \"[Gmail]/Bin\"");
            }
            else if (command.Contains("\"%\""))
            {
                await w.WriteLineAsync("* LIST (\\HasNoChildren) \"/\" \"INBOX\"");
                await w.WriteLineAsync("* LIST (\\Noselect \\HasChildren) \"/\" \"[Gmail]\"");
                await w.WriteLineAsync($"* LIST (\\HasNoChildren \\Sent) \"/\" \"{sent}\"");
            }
            else if (command.Contains("INBOX"))
            {
                await w.WriteLineAsync("* LIST (\\HasNoChildren) \"/\" \"INBOX\"");
            }
            else if (command.Contains("\"\" \"\""))
            {
                await w.WriteLineAsync("* LIST (\\Noselect) \"/\" \"\"");
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try { await _loop; } catch (Exception) { /* already reported through the assertions */ }
            _stop.Dispose();
        }
    }
}

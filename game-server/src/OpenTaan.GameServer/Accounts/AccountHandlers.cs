using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.Connection;

namespace OpenTaan.GameServer.Accounts;

/// <summary>Checking, creating and logging in to accounts, and opening the connection a login arrives on.</summary>
/// <remarks>
/// The client plays up to two accounts at once, one per slot, and names the slot in
/// each request. A slot outside the two can only come from a modified client. It is
/// never echoed, because the client picks the step of its login flow to complete from
/// the slot it gets back, and it is never left unanswered, because that step would
/// then wait for good. The reply goes to slot 0 instead.
///
/// Email addresses are never logged: they identify a person.
/// </remarks>
public sealed partial class AccountHandlers(
    IAccountStore store,
    IOptions<AccountOptions> options,
    ILogger<AccountHandlers> logger)
{
    private const int SlotCount = 2;
    private const byte SafeSlot = 0;

    /// <summary>
    /// Answers the request the client opens its login connection with, letting it go on
    /// to log in. The request describes the client and is not decoded; see
    /// <see cref="PqFirstPacket"/>.
    /// </summary>
    public ValueTask FirstPacketAsync(ClientSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        session.Send(new PrFirstPacket(Status: 0, ServerValue: 1));
        return ValueTask.CompletedTask;
    }

    /// <summary>Answers whether an account exists.</summary>
    public async ValueTask CheckAccountAsync(ClientSession session, PqCheckAccount request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (!IsSlot(request.AccountSlot))
        {
            LogInvalidSlot(logger, session.Id, nameof(PqCheckAccount), request.AccountSlot);
            session.Send(new PrCheckAccount(CheckAccountStatus.NotFound, SafeSlot));
            return;
        }

        var account = await store.FindAsync(request.EmailAddress, cancellationToken).ConfigureAwait(false);
        var status = account is null ? CheckAccountStatus.NotFound : CheckAccountStatus.Exists;
        session.Send(new PrCheckAccount(status, request.AccountSlot));
    }

    /// <summary>Creates an account, unless its address is taken.</summary>
    public async ValueTask CreateAccountAsync(ClientSession session, PqCreateAccount request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (!IsSlot(request.AccountSlot))
        {
            LogInvalidSlot(logger, session.Id, nameof(PqCreateAccount), request.AccountSlot);
            session.Send(new PrCreateAccount(CreateAccountStatus.Failed, SafeSlot));
            return;
        }

        var created = await store.CreateAsync(
            request.EmailAddress,
            request.PasswordHash,
            Chosen(request.Gender),
            cancellationToken).ConfigureAwait(false);

        var status = created is null ? CreateAccountStatus.AlreadyExists : CreateAccountStatus.Created;
        session.Send(new PrCreateAccount(status, request.AccountSlot));
    }

    /// <summary>
    /// Logs in every account the request names, or none of them: the answer has one
    /// success flag for all of them.
    /// </summary>
    /// <remarks>
    /// An account is live on one connection at a time. A login naming one account in
    /// both slots, or an account another connection holds, is refused as already in the
    /// game, which returns the client to its login form. The newcomer is refused; the
    /// connection already playing is never closed for it, so knowing a password is not
    /// enough to throw a player out. Both checks come after the passwords, so a login
    /// attempt cannot tell anyone whether an account is online.
    /// </remarks>
    public async ValueTask UserLoginAsync(ClientSession session, PqUserLogin request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (session.IsLoggedIn)
        {
            LogSecondLogin(logger, session.Id);
            session.Close(CloseReason.SecondLogin);
            return;
        }

        if (request.Accounts.Count is 0 or > SlotCount)
        {
            LogInvalidAccountCount(logger, session.Id, request.Accounts.Count);
            session.Send(new PrUserLoginFailed(LoginRefusalReason.UnknownAccount));
            return;
        }

        var accounts = new List<Account>(request.Accounts.Count);
        foreach (var credential in request.Accounts)
        {
            var (account, refusal) = await AuthenticateAsync(credential, cancellationToken).ConfigureAwait(false);
            if (account is null)
            {
                LogRefused(logger, session.Id, refusal, accounts.Count);
                session.Send(new PrUserLoginFailed(refusal));
                return;
            }

            accounts.Add(account);
        }

        // By account, not by address, so this holds however the store compares addresses.
        if (accounts.DistinctBy(account => account.AccountId).Count() != accounts.Count)
        {
            LogSameAccountTwice(logger, session.Id);
            session.Send(new PrUserLoginFailed(LoginRefusalReason.AlreadyInGame));
            return;
        }

        var holder = session.TryLogIn(accounts);
        if (holder is not null)
        {
            LogAlreadyInGame(logger, session.Id, holder.Id);
            session.Send(new PrUserLoginFailed(LoginRefusalReason.AlreadyInGame));
            return;
        }

        LogLoggedIn(logger, session.Id, accounts.Count);

        session.Send(new PrUserLoginSucceeded(
            LocalPlayerId: accounts[0].LocalPlayerId,
            Field24: 0,
            Accounts: [.. accounts.Select(account =>
                new AccountInfo(account.AccountId, TypeOf(account), 0, 0, account.Gender))],
            Catalogs: [.. accounts.Select(_ => CharacterCatalog.Empty)],
            Field28: 0,
            Field2C: string.Empty,
            Field30: 0));
    }

    private async ValueTask<(Account? Account, LoginRefusalReason Refusal)> AuthenticateAsync(
        LoginAccountInfo credential,
        CancellationToken cancellationToken)
    {
        if (!options.Value.RequireRegistration)
        {
            var provisioned = await store.FindOrCreateAsync(
                credential.EmailAddress,
                credential.PasswordHash,
                Gender.Male,
                cancellationToken).ConfigureAwait(false);
            return (provisioned, default);
        }

        var account = await store.FindAsync(credential.EmailAddress, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return (null, LoginRefusalReason.UnknownAccount);
        }

        return account.HasPassword(credential.PasswordHash)
            ? (account, default)
            : (null, LoginRefusalReason.WrongPassword);
    }

    private AccountType TypeOf(Account account) =>
        options.Value.GameMasters.Contains(account.EmailAddress, StringComparer.Ordinal)
            ? AccountType.GameMaster
            : AccountType.Normal;

    private static bool IsSlot(byte slot) => slot < SlotCount;

    // An account sent to the client without a gender gets a character creator with no
    // appearance options, so one a modified client leaves unset becomes male.
    private static Gender Chosen(Gender gender) =>
        gender is Gender.Male or Gender.Female ? gender : Gender.Male;

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} sent {Packet} for account slot {Slot}, which no real client has; answered for slot 0")]
    private static partial void LogInvalidSlot(ILogger logger, long connectionId, string packet, byte slot);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} sent a login for {Count} accounts; a real client sends one or two")]
    private static partial void LogInvalidAccountCount(ILogger logger, long connectionId, int count);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} sent a second login after logging in; closing it")]
    private static partial void LogSecondLogin(ILogger logger, long connectionId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Connection {ConnectionId}: login refused ({Reason}) at account {Position}")]
    private static partial void LogRefused(ILogger logger, long connectionId, LoginRefusalReason reason, int position);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId}: login refused, one account named in both slots")]
    private static partial void LogSameAccountTwice(ILogger logger, long connectionId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId}: login refused, an account is live on connection {HolderId}")]
    private static partial void LogAlreadyInGame(ILogger logger, long connectionId, long holderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection {ConnectionId} logged in with {Accounts} account(s)")]
    private static partial void LogLoggedIn(ILogger logger, long connectionId, int accounts);
}

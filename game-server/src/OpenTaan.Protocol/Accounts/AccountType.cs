namespace OpenTaan.Protocol.Accounts;

/// <summary>What kind of account an <see cref="AccountInfo"/> describes.</summary>
public enum AccountType : ushort
{
    /// <summary>A player.</summary>
    Normal = 0,

    /// <summary>A game master.</summary>
    GameMaster = 0xFF,

    /// <summary>No account in this slot.</summary>
    Empty = 0xFFFF,
}

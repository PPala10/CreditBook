namespace CreditBook.Storage;

/// <summary>A desktop owns this for its lifetime; backup/read-only access stays available.</summary>
public sealed class LedgerSession : IDisposable
{
    private readonly FileStream writerLock;
    public Ledger Ledger { get; }

    public LedgerSession(string databasePath)
    {
        var path = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try { writerLock = new FileStream(path + ".writer.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("Bu defter başka bir CreditBook penceresinde açık. Önce diğer pencereyi kapatın.",ex); }
        try { Ledger = new Ledger(path); }
        catch { writerLock.Dispose(); throw; }
    }

    public void Dispose() => writerLock.Dispose();
}

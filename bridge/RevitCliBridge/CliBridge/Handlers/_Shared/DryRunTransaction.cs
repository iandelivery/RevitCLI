using Autodesk.Revit.DB;
using System;

namespace RevitCliBridge.Handlers
{
    /// <summary>
    /// Transaction wrapper that automatically rolls back on Commit when dry-run mode is active.
    /// Use this in command handlers that support --dry-run instead of raw <see cref="Transaction"/>.
    /// <para>
    /// Usage:
    /// <code>
    /// using (var tx = new DryRunTransaction(doc, "Create Wall", cmd.DryRun))
    /// {
    ///     // ... modify the document ...
    ///     tx.Commit(); // rolls back if DryRun is true
    /// }
    /// </code>
    /// </para>
    /// </summary>
    public class DryRunTransaction : IDisposable
    {
        private readonly Transaction _transaction;
        private readonly bool _dryRun;
        private bool _committed;

        /// <summary>
        /// Whether this transaction is in dry-run mode.
        /// </summary>
        public bool IsDryRun => _dryRun;

        /// <summary>
        /// Creates a new transaction that will auto-rollback on Commit if dryRun is true.
        /// </summary>
        public DryRunTransaction(Document doc, string name, bool dryRun)
        {
            _dryRun = dryRun;
            var txName = dryRun ? $"[DRY-RUN] {name}" : name;
            _transaction = new Transaction(doc, txName);
            _transaction.Start();
        }

        /// <summary>
        /// Commits the transaction. In dry-run mode, rolls back instead.
        /// </summary>
        public void Commit()
        {
            if (_dryRun)
            {
                _transaction.RollBack();
                _committed = true;
                return;
            }
            _transaction.Commit();
            _committed = true;
        }

        /// <summary>
        /// Returns the transaction status.
        /// </summary>
        public TransactionStatus GetStatus()
        {
            return _transaction.GetStatus();
        }

        public void Dispose()
        {
            if (!_committed)
                _transaction.RollBack();
            _transaction.Dispose();
        }
    }
}

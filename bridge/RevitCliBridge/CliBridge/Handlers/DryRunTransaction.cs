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
        /// Commits (or rolls back in dry-run mode) AFTER evaluating
        /// <paramref name="resultSnapshot"/>, so handlers can safely read
        /// element state (Id, Name, parameters) that becomes invalid after
        /// a dry-run rollback. The evaluation order — snapshot first, then
        /// commit — is enforced by this API, removing the temporal hazard
        /// from every call site:
        /// <code>
        /// var wall = Wall.Create(doc, line, levelId, false);
        /// var result = tx.Commit(() => new { element_id = wall.Id.IntegerValue });
        /// </code>
        /// </summary>
        public TResult Commit<TResult>(Func<TResult> resultSnapshot)
        {
            if (resultSnapshot is null)
                throw new ArgumentNullException(nameof(resultSnapshot));

            TResult result = resultSnapshot();
            Commit();
            return result;
        }

        /// <summary>
        /// Returns <paramref name="dryRunMessage"/> in dry-run mode,
        /// otherwise <paramref name="message"/> — for response text that
        /// reflects what actually happened to the transaction.
        /// </summary>
        public string Describe(string dryRunMessage, string message)
        {
            return _dryRun ? dryRunMessage : message;
        }

        /// <summary>
        /// Rolls back the transaction unconditionally (handler error paths).
        /// Marks the transaction as handled so Dispose will not roll back again.
        /// </summary>
        public void RollBack()
        {
            _transaction.RollBack();
            _committed = true;
        }

        /// <summary>
        /// Returns the transaction status.
        /// </summary>
        public TransactionStatus GetStatus()
        {
            return _transaction.GetStatus();
        }

        /// <summary>
        /// Applies the CLI failure-handling options (CliFailurePreprocessor)
        /// to the wrapped transaction. Equivalent to calling
        /// <c>t.ConfigureFailureHandling()</c> on a raw Transaction.
        /// </summary>
        public void ConfigureFailureHandling()
        {
            _transaction.ConfigureFailureHandling();
        }

        public void Dispose()
        {
            if (!_committed)
                _transaction.RollBack();
            _transaction.Dispose();
        }
    }
}

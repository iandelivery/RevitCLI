using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitCliBridge.Handlers.Mep
{
    /// <summary>
    /// Unified connector-pair resolution shared by every MEP fitting handler.
    /// Generic over the concrete MEP curve type (Duct / CableTray / Pipe) so a
    /// single implementation serves all disciplines — only the element label
    /// used in error messages and the known connector count differ. Replaces
    /// the former per-namespace DuctFittingHelper and FittingHelper.
    /// </summary>
    internal static class MepFittingResolver
    {
        /// <summary>
        /// Resolve two elements by element ID and pick a connector from each.
        /// When connector indices are null, the closest matching pair is
        /// auto-selected; otherwise the specified index is used (0 or 1).
        /// Returns (c1, c2, error) where error is null on success.
        /// </summary>
        /// <typeparam name="T">Concrete MEP curve type (Duct/CableTray/Pipe).</typeparam>
        /// <param name="elementLabel">Singular label used in error messages, e.g. "duct" or "cable tray".</param>
        /// <param name="knownConnectorCount">Expected connector count for range-check messages (2 for straight runs).</param>
        public static (Connector? c1, Connector? c2, string? error) ResolveConnectorPair<T>(
            Document doc, int elementId1, int elementId2,
            int? connectorIndex1, int? connectorIndex2,
            string elementLabel, int knownConnectorCount)
            where T : MEPCurve
        {
            if (elementId1 == elementId2)
                return (null, null, "Both element IDs refer to the same element. Fittings require two distinct elements.");

            var e1 = doc.GetElement(new ElementId(elementId1)) as T;
            var e2 = doc.GetElement(new ElementId(elementId2)) as T;
            if (e1 is null || e2 is null)
                return (null, null, $"One or both element IDs do not refer to a {elementLabel}.");

            // Auto-select closest pair when no indices provided.
            if (!connectorIndex1.HasValue && !connectorIndex2.HasValue)
            {
                var pair = MepCurveGeometry.FindClosestConnectors(e1, e2);
                if (pair is null)
                    return (null, null, $"No connector pair with matching domain found between the two {elementLabel}s.");
                return (pair.Value.a, pair.Value.b, null);
            }

            // Explicit indices — validate range and pick.
            var c1 = PickByIndex(e1, connectorIndex1);
            var c2 = PickByIndex(e2, connectorIndex2);

            if (c1 is null && connectorIndex1.HasValue)
                return (null, null, $"connector_index_1={connectorIndex1.Value} is out of range ({elementLabel}s have {knownConnectorCount} connectors: 0 and {knownConnectorCount - 1}).");
            if (c2 is null && connectorIndex2.HasValue)
                return (null, null, $"connector_index_2={connectorIndex2.Value} is out of range ({elementLabel}s have {knownConnectorCount} connectors: 0 and {knownConnectorCount - 1}).");

            c1 ??= MepCurveGeometry.FindClosestConnectors(e1, e2)?.a;
            c2 ??= MepCurveGeometry.FindClosestConnectors(e1, e2)?.b;

            if (c1 is null || c2 is null)
                return (null, null, "Could not resolve a connector pair.");

            return (c1, c2, null);
        }

        private static Connector? PickByIndex(MEPCurve element, int? index)
        {
            if (!index.HasValue) return null;
            var list = new List<Connector>();
            foreach (Connector c in element.ConnectorManager.Connectors)
                list.Add(c);
            if (index.Value < 0 || index.Value >= list.Count)
                return null;
            return list[index.Value];
        }
    }
}
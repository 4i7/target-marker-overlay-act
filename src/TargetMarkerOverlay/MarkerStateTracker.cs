using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TargetMarkerOverlay
{
    public sealed class MarkerStateTracker
    {
        private readonly object sync = new object();
        private readonly Dictionary<int, MarkerAssignment> byMarker = new Dictionary<int, MarkerAssignment>();
        private readonly Dictionary<uint, CombatantInfo> combatants = new Dictionary<uint, CombatantInfo>();
        public string Language { get; set; } = Localization.English;
        public event EventHandler StateChanged;
        public event EventHandler<string> Activity;

        public void ProcessLine(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            try
            {
                if (raw.IndexOf('|') >= 0) ProcessNetwork(raw.Trim());
                else ProcessParsed(raw.Trim());
            }
            catch (Exception ex)
            {
                Activity?.Invoke(this, Localization.Get(Language, "ParseError", ex.Message));
            }
        }

        private void ProcessNetwork(string raw)
        {
            var p = raw.Split('|');
            if (p.Length < 2) return;
            switch (p[0])
            {
                case "01": Clear(Localization.Get(Language, "ZoneCleared")); break;
                case "02": Clear(Localization.Get(Language, "PlayerCleared")); break;
                case "03":
                    if (p.Length >= 6) UpsertCombatant(ParseHex(p[2]), p[3], ParseHexInt(p[4]));
                    break;
                case "04":
                    if (p.Length >= 3) RemoveCombatant(ParseHex(p[2]));
                    break;
                case "29":
                    if (p.Length >= 8) ApplyMarker(p[2], ParseInt(p[3]), ParseHex(p[6]), p[7]);
                    break;
                case "250": Clear(Localization.Get(Language, "ProcessCleared")); break;
            }
        }

        private void ProcessParsed(string raw)
        {
            var sign = raw.IndexOf("SignMarker 1D:", StringComparison.OrdinalIgnoreCase);
            if (sign >= 0)
            {
                var p = raw.Substring(sign + "SignMarker 1D:".Length).Split(':');
                if (p.Length >= 6) ApplyMarker(p[0], ParseInt(p[1]), ParseHex(p[4]), p[5]);
                return;
            }

            var add = raw.IndexOf("AddCombatant 03:", StringComparison.OrdinalIgnoreCase);
            if (add >= 0)
            {
                var p = raw.Substring(add + "AddCombatant 03:".Length).Split(':');
                if (p.Length >= 3) UpsertCombatant(ParseHex(p[0]), p[1], ParseHexInt(p[2]));
                return;
            }

            var remove = raw.IndexOf("RemoveCombatant 04:", StringComparison.OrdinalIgnoreCase);
            if (remove >= 0)
            {
                var p = raw.Substring(remove + "RemoveCombatant 04:".Length).Split(':');
                if (p.Length >= 1) RemoveCombatant(ParseHex(p[0]));
                return;
            }

            if (raw.IndexOf("Territory 01:", StringComparison.OrdinalIgnoreCase) >= 0) Clear(Localization.Get(Language, "ZoneCleared"));
            else if (raw.IndexOf("ChangePrimaryPlayer 02:", StringComparison.OrdinalIgnoreCase) >= 0) Clear(Localization.Get(Language, "PlayerCleared"));
            else if (raw.IndexOf("Process ", StringComparison.OrdinalIgnoreCase) >= 0) Clear(Localization.Get(Language, "ProcessCleared"));
        }

        private void ApplyMarker(string operation, int markerCode, uint targetId, string targetName)
        {
            var changed = false;
            lock (sync)
            {
                if (operation.Equals("Delete", StringComparison.OrdinalIgnoreCase))
                {
                    changed = byMarker.Remove(markerCode);
                    if (targetId != 0)
                    {
                        foreach (var key in byMarker.Where(x => x.Value.TargetId == targetId).Select(x => x.Key).ToArray())
                            changed |= byMarker.Remove(key);
                    }
                }
                else if (operation.Equals("Add", StringComparison.OrdinalIgnoreCase) || operation.Equals("Update", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var key in byMarker.Where(x => x.Value.TargetId == targetId && x.Key != markerCode).Select(x => x.Key).ToArray())
                        byMarker.Remove(key);
                    CombatantInfo combatant;
                    combatants.TryGetValue(targetId, out combatant);
                    byMarker[markerCode] = new MarkerAssignment
                    {
                        MarkerCode = markerCode,
                        TargetId = targetId,
                        TargetName = string.IsNullOrWhiteSpace(targetName) ? (combatant?.Name ?? "Unknown") : targetName,
                        JobId = combatant?.JobId ?? 0,
                        UpdatedAt = DateTime.UtcNow,
                    };
                    changed = true;
                }
            }
            if (changed)
            {
                Activity?.Invoke(this, operation + " / " + MarkerCatalog.Get(markerCode).Label + " / " + (targetName ?? ""));
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void UpsertCombatant(uint id, string name, int jobId)
        {
            if (id == 0) return;
            var changed = false;
            lock (sync)
            {
                combatants[id] = new CombatantInfo { Id = id, Name = name, JobId = jobId };
                foreach (var marker in byMarker.Values.Where(x => x.TargetId == id))
                {
                    marker.JobId = jobId;
                    if (!string.IsNullOrWhiteSpace(name)) marker.TargetName = name;
                    changed = true;
                }
            }
            if (changed) StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RemoveCombatant(uint id)
        {
            var changed = false;
            lock (sync)
            {
                combatants.Remove(id);
                foreach (var key in byMarker.Where(x => x.Value.TargetId == id).Select(x => x.Key).ToArray())
                    changed |= byMarker.Remove(key);
            }
            if (changed) StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Clear(string reason = null)
        {
            var changed = false;
            lock (sync)
            {
                changed = byMarker.Count > 0;
                byMarker.Clear();
                combatants.Clear();
            }
            Activity?.Invoke(this, reason ?? Localization.Get(Language, "StateCleared"));
            if (changed) StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public List<MarkerAssignment> Snapshot()
        {
            lock (sync)
            {
                return byMarker.Values.Select(x => new MarkerAssignment
                {
                    MarkerCode = x.MarkerCode, TargetId = x.TargetId, TargetName = x.TargetName,
                    JobId = x.JobId, UpdatedAt = x.UpdatedAt,
                }).ToList();
            }
        }

        private static uint ParseHex(string value)
        {
            uint result;
            return uint.TryParse((value ?? "").Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result) ? result : 0;
        }

        private static int ParseHexInt(string value)
        {
            int result;
            return int.TryParse((value ?? "").Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result) ? result : 0;
        }

        private static int ParseInt(string value)
        {
            int result;
            return int.TryParse((value ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : 0;
        }
    }
}

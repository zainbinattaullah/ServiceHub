using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace ServiceHub.Controllers
{
    public class TimeWindowService
    {
        private readonly List<TimeSpan> _runTimes;
        private readonly List<TimeSpan> _transferTimes;

        public TimeWindowService(IConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            _runTimes      = ParseTimes(configuration.GetValue<string>("RunTimes"));
            _transferTimes = ParseTimes(configuration.GetValue<string>("TransferRunTimes"));
        }

        // ── Window model ─────────────────────────────────────────────────────
        // The 24-hour day is divided into alternating Attendance / Transfer slots
        // by merging RunTimes and TransferRunTimes into one sorted event list.
        // The current window = whichever event fired last (edge-triggered).
        // No RunWindowMinutes / TransferWindowMinutes needed or used.
        //
        // Example  RunTimes=08:00,10:00   TransferRunTimes=09:15,11:15
        //   08:00→Attendance  09:15→Transfer  10:00→Attendance  11:15→Transfer
        //   At 08:30 → Attendance   At 09:45 → Transfer   At 07:00 → Transfer (wrap)
        // ─────────────────────────────────────────────────────────────────────

        public enum WindowKind { Attendance, Transfer }

        public class WindowSlot
        {
            public WindowKind Kind    { get; set; }
            public TimeSpan   Start   { get; set; }
            public TimeSpan   End     { get; set; }   // start of next event
            public bool       IsCurrent { get; set; }
        }

        // ── Public API ───────────────────────────────────────────────────────

        public bool IsTransferWindowOpen()  => CurrentWindow() == WindowKind.Transfer;
        public bool IsAttendanceWindowOpen() => CurrentWindow() == WindowKind.Attendance;

        public List<TimeSpan> GetRunTimes()      => _runTimes;
        public List<TimeSpan> GetTransferTimes() => _transferTimes;

        /// Human-readable message listing all transfer slots.
        public string GetTransferWindowMessage()
        {
            if (!_transferTimes.Any())
                return "No transfer windows configured.";

            var slots = GetAllWindows().Where(w => w.Kind == WindowKind.Transfer)
                                       .Select(w => $"{Fmt(w.Start)} to {Fmt(w.End)}");
            return $"Transfer allowed during: {string.Join(", ", slots)}";
        }

        /// Time remaining until the next window boundary (the next event in the schedule).
        public TimeSpan? GetNextWindowChange()
        {
            var events = SortedEvents();
            if (events.Count == 0) return null;

            var now = DateTime.Now.TimeOfDay;
            foreach (var ev in events)
                if (ev.At > now) return ev.At - now;

            // No more events today — time until first event tomorrow
            return events[0].At.Add(TimeSpan.FromDays(1)) - now;
        }

        /// Full schedule: every attendance and transfer slot in order.
        public List<WindowSlot> GetAllWindows()
        {
            var events = SortedEvents();
            if (events.Count == 0) return new List<WindowSlot>();

            var now    = DateTime.Now.TimeOfDay;
            var result = new List<WindowSlot>(events.Count);

            for (int i = 0; i < events.Count; i++)
            {
                var start = events[i].At;
                // End = next event's start time (last slot wraps to first event next day)
                var end   = i + 1 < events.Count ? events[i + 1].At : events[0].At;

                bool isCurrent = i + 1 < events.Count
                    ? now >= start && now < end
                    : now >= start || now < events[0].At; // midnight-wrap slot

                result.Add(new WindowSlot
                {
                    Kind      = events[i].Kind,
                    Start     = start,
                    End       = end,
                    IsCurrent = isCurrent
                });
            }
            return result;
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private WindowKind CurrentWindow()
        {
            var events = SortedEvents();
            if (events.Count == 0) return WindowKind.Transfer;

            var now     = DateTime.Now.TimeOfDay;
            var current = events[events.Count - 1].Kind; // wrap default = last event prev day

            foreach (var ev in events)
            {
                if (ev.At <= now) current = ev.Kind;
                else break;
            }
            return current;
        }

        private List<(TimeSpan At, WindowKind Kind)> SortedEvents()
        {
            var list = new List<(TimeSpan, WindowKind)>();
            foreach (var t in _runTimes)      list.Add((t, WindowKind.Attendance));
            foreach (var t in _transferTimes) list.Add((t, WindowKind.Transfer));
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return list;
        }

        private static List<TimeSpan> ParseTimes(string csv) =>
            string.IsNullOrWhiteSpace(csv)
                ? new List<TimeSpan>()
                : csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                     .Select(s => TimeSpan.Parse(s.Trim()))
                     .OrderBy(t => t)
                     .ToList();

        private static string Fmt(TimeSpan t)
        {
            if (t >= TimeSpan.FromHours(24)) t -= TimeSpan.FromHours(24);
            return DateTime.Today.Add(t).ToString("hh:mm tt");
        }
    }
}

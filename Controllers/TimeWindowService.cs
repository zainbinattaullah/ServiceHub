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

            var runTimesStr = configuration.GetValue<string>("RunTimes");
            _runTimes = string.IsNullOrWhiteSpace(runTimesStr)
                ? new List<TimeSpan>()
                : runTimesStr
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => TimeSpan.Parse(s.Trim()))
                    .OrderBy(t => t)
                    .ToList();

            var transferTimesStr = configuration.GetValue<string>("TransferRunTimes");
            _transferTimes = string.IsNullOrWhiteSpace(transferTimesStr)
                ? new List<TimeSpan>()
                : transferTimesStr
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => TimeSpan.Parse(s.Trim()))
                    .OrderBy(t => t)
                    .ToList();
        }

        public bool IsTransferWindowOpen()
        {
            if (!_runTimes.Any() || !_transferTimes.Any())
            {
                return false;
            }

            var now = DateTime.Now.TimeOfDay;

            // Find the next runtime after each transfer time
            foreach (var transferTime in _transferTimes)
            {
                var nextRuntime = _runTimes.FirstOrDefault(r => r > transferTime);
                var windowEnd = nextRuntime != default ? nextRuntime : _runTimes.First().Add(TimeSpan.FromDays(1));

                if (now >= transferTime && now < windowEnd)
                {
                    return true;
                }
            }

            return false;
        }

        public string GetTransferWindowMessage()
        {
            if (!_transferTimes.Any() || !_runTimes.Any())
            {
                return "No transfer windows configured.";
            }

            var windows = new List<string>();

            foreach (var transferTime in _transferTimes)
            {
                var nextRuntime = _runTimes.FirstOrDefault(r => r > transferTime);
                var windowEnd = nextRuntime != default ? nextRuntime : _runTimes.First().Add(TimeSpan.FromDays(1));

                windows.Add($"{transferTime:hh\\:mm} to {windowEnd:hh\\:mm}");
            }

            return $"Transfer allowed during: {string.Join(", ", windows)}";
        }

        public TimeSpan? GetNextWindowChange()
        {
            var now = DateTime.Now.TimeOfDay;

            // If currently in a transfer window, return time until it ends
            if (_transferTimes.Any() && _runTimes.Any())
            {
                foreach (var transferTime in _transferTimes)
                {
                    var nextRuntime = _runTimes.FirstOrDefault(r => r > transferTime);
                    var windowEnd = nextRuntime != default ? nextRuntime : _runTimes.First().Add(TimeSpan.FromDays(1));

                    if (now >= transferTime && now < windowEnd)
                    {
                        return windowEnd - now;
                    }
                }
            }

            // Otherwise find when next transfer window starts
            var nextTransfer = _transferTimes.FirstOrDefault(t => t > now);
            if (nextTransfer != default)
            {
                return nextTransfer - now;
            }

            // If no more today, return time until first transfer tomorrow
            if (_transferTimes.Any())
            {
                return _transferTimes.First().Add(TimeSpan.FromDays(1)) - now;
            }

            return null;
        }
        public List<TimeSpan> GetRunTimes() => _runTimes;
        public List<TimeSpan> GetTransferTimes() => _transferTimes;

    }
}

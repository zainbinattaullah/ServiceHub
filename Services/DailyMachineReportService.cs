using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceHub.Data;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace ServiceHub.Services
{
    /// <summary>
    /// Background service that sends a daily machine-log email report
    /// at the configured time (default 14:00).
    /// </summary>
    public class DailyMachineReportService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly SmtpSettings _smtp;
        private readonly DailyReportSettings _settings;
        private readonly ILogger<DailyMachineReportService> _logger;

        public DailyMachineReportService(
            IServiceScopeFactory scopeFactory,
            IOptions<SmtpSettings> smtpOptions,
            IOptions<DailyReportSettings> reportOptions,
            ILogger<DailyMachineReportService> logger)
        {
            _scopeFactory = scopeFactory;
            _smtp = smtpOptions.Value;
            _settings = reportOptions.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("DailyMachineReportService started. Scheduled at {Time} daily → {Recipients}",
                _settings.SendTime, string.Join(", ", _settings.Recipients));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    var scheduledTime = DateTime.Today.Add(TimeSpan.Parse(_settings.SendTime));

                    // If today's time already passed, schedule for tomorrow
                    if (now > scheduledTime)
                        scheduledTime = scheduledTime.AddDays(1);

                    var delay = scheduledTime - now;
                    _logger.LogInformation("Next machine report scheduled at {ScheduledTime} (in {Delay})",
                        scheduledTime, delay);

                    await Task.Delay(delay, stoppingToken);

                    await SendDailyReportAsync(stoppingToken);
                }
                catch (TaskCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DailyMachineReportService encountered an error. Retrying in 60s...");
                    await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
                }
            }
        }

        private async Task SendDailyReportAsync(CancellationToken ct)
        {
            _logger.LogInformation("Generating daily machine connection report...");

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubContext>();

            // ── Get today's date range ────────────────────────────────────
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);

            // ── Fetch today's connection logs ─────────────────────────────
            var logs = await db.AttendenceMachineConnectionLogs
                .AsNoTracking()
                .Where(l => l.Connection_StartTime >= today && l.Connection_StartTime < tomorrow)
                .OrderByDescending(l => l.Connection_StartTime)
                .ToListAsync(ct);

            // ── Fetch all registered machines for cross-reference ─────────
            var machines = await db.AttendenceMachines
                .AsNoTracking()
                .Where(m => m.IsActive)
                .ToListAsync(ct);

            var machineNameMap = machines.ToDictionary(m => m.IpAddress, m => m.Name);

            // ── Separate failed/offline vs successful ─────────────────────
            var failedLogs = logs.Where(l =>
                l.Status != null &&
                !l.Status.Equals("Connected", StringComparison.OrdinalIgnoreCase) &&
                !l.Status.Equals("Success", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var successLogs = logs.Where(l =>
                l.Status != null &&
                (l.Status.Equals("Connected", StringComparison.OrdinalIgnoreCase) ||
                 l.Status.Equals("Success", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // ── Machines with NO logs today (never connected) ─────────────
            var loggedIPs = logs.Select(l => l.Machine_IP).Distinct().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var neverConnected = machines.Where(m => !loggedIPs.Contains(m.IpAddress)).ToList();

            // ── Build HTML email ──────────────────────────────────────────
            var sb = new StringBuilder();
            sb.Append($@"
<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<style>
    body {{ font-family: 'Segoe UI', Arial, sans-serif; background: #f4f6f9; margin: 0; padding: 20px; }}
    .container {{ max-width: 900px; margin: 0 auto; background: #fff; border-radius: 12px; box-shadow: 0 4px 24px rgba(0,0,0,0.08); overflow: hidden; }}
    .header {{ background: linear-gradient(135deg, #1e3a5f, #2d5a96); color: #fff; padding: 24px 30px; }}
    .header h1 {{ margin: 0 0 6px; font-size: 22px; }}
    .header p {{ margin: 0; opacity: 0.85; font-size: 14px; }}
    .content {{ padding: 24px 30px; }}
    .stat-row {{ display: flex; gap: 16px; margin-bottom: 24px; flex-wrap: wrap; }}
    .stat-card {{ flex: 1; min-width: 140px; background: #f8fafc; border-radius: 10px; padding: 16px; text-align: center; border: 1px solid #e2e8f0; }}
    .stat-card .num {{ font-size: 28px; font-weight: 700; }}
    .stat-card .lbl {{ font-size: 12px; color: #64748b; text-transform: uppercase; letter-spacing: 0.5px; margin-top: 4px; }}
    .stat-card.danger .num {{ color: #dc3545; }}
    .stat-card.success .num {{ color: #198754; }}
    .stat-card.warning .num {{ color: #f59e0b; }}
    .stat-card.info .num {{ color: #0d6efd; }}
    h3 {{ color: #1e293b; font-size: 16px; margin: 24px 0 10px; border-bottom: 2px solid #e2e8f0; padding-bottom: 6px; }}
    table {{ width: 100%; border-collapse: collapse; font-size: 13px; margin-bottom: 20px; }}
    th {{ background: #1e3a5f; color: #fff; padding: 10px 12px; text-align: left; font-weight: 600; }}
    td {{ padding: 8px 12px; border-bottom: 1px solid #e2e8f0; color: #334155; }}
    tr:nth-child(even) td {{ background: #f8fafc; }}
    .badge {{ display: inline-block; padding: 3px 10px; border-radius: 12px; font-size: 11px; font-weight: 600; }}
    .badge-danger {{ background: #fde8e8; color: #dc3545; }}
    .badge-success {{ background: #d1f2eb; color: #198754; }}
    .badge-warning {{ background: #fff3cd; color: #856404; }}
    .footer {{ background: #f8fafc; padding: 16px 30px; text-align: center; font-size: 12px; color: #94a3b8; border-top: 1px solid #e2e8f0; }}
    .error-msg {{ color: #dc3545; font-size: 12px; max-width: 250px; word-break: break-word; }}
</style>
</head>
<body>
<div class='container'>
    <div class='header'>
        <h1>&#128225; Daily Machine Connection Report</h1>
        <p>{today:dddd, dd MMMM yyyy} &mdash; Generated at {DateTime.Now:hh:mm tt}</p>
    </div>
    <div class='content'>

        <div class='stat-row'>
            <div class='stat-card info'>
                <div class='num'>{machines.Count}</div>
                <div class='lbl'>Active Machines</div>
            </div>
            <div class='stat-card success'>
                <div class='num'>{successLogs.Select(l => l.Machine_IP).Distinct().Count()}</div>
                <div class='lbl'>Connected Today</div>
            </div>
            <div class='stat-card danger'>
                <div class='num'>{failedLogs.Select(l => l.Machine_IP).Distinct().Count()}</div>
                <div class='lbl'>Failed Connections</div>
            </div>
            <div class='stat-card warning'>
                <div class='num'>{neverConnected.Count}</div>
                <div class='lbl'>Never Connected Today</div>
            </div>
        </div>
");

            // ── Section: Machines that never connected today ──────────────
            if (neverConnected.Any())
            {
                sb.Append(@"<h3>&#9888;&#65039; Machines With No Connection Today</h3>");
                sb.Append("<table><tr><th>#</th><th>Machine Name</th><th>IP Address</th><th>Location</th><th>Device Model</th></tr>");
                int idx = 1;
                foreach (var m in neverConnected)
                {
                    sb.Append($@"<tr>
                        <td>{idx++}</td>
                        <td><strong>{Encode(m.Name)}</strong></td>
                        <td>{Encode(m.IpAddress)}</td>
                        <td>{Encode(m.Location ?? "—")}</td>
                        <td>{Encode(m.DeviceModel ?? "—")}</td>
                    </tr>");
                }
                sb.Append("</table>");
            }

            // ── Section: Failed / Offline connection logs ─────────────────
            if (failedLogs.Any())
            {
                sb.Append(@"<h3>&#10060; Failed / Offline Connection Logs</h3>");
                sb.Append("<table><tr><th>#</th><th>Machine Name</th><th>Machine IP</th><th>Start Time</th><th>End Time</th><th>Status</th><th>Error Message</th><th>Records Read</th></tr>");
                int idx = 1;
                foreach (var l in failedLogs)
                {
                    machineNameMap.TryGetValue(l.Machine_IP, out var name);
                    sb.Append($@"<tr>
                        <td>{idx++}</td>
                        <td><strong>{Encode(name ?? "Unknown")}</strong></td>
                        <td>{Encode(l.Machine_IP)}</td>
                        <td>{l.Connection_StartTime:dd-MMM-yyyy HH:mm:ss}</td>
                        <td>{(l.Connection_EndTime.HasValue ? l.Connection_EndTime.Value.ToString("dd-MMM-yyyy HH:mm:ss") : "—")}</td>
                        <td><span class='badge badge-danger'>{Encode(l.Status)}</span></td>
                        <td class='error-msg'>{Encode(l.ErrorMessage ?? "—")}</td>
                        <td>{l.RecordsRead?.ToString() ?? "0"}</td>
                    </tr>");
                }
                sb.Append("</table>");
            }

            // ── Section: Successful connection logs ───────────────────────
            if (successLogs.Any())
            {
                sb.Append(@"<h3>&#9989; Successful Connection Logs</h3>");
                sb.Append("<table><tr><th>#</th><th>Machine Name</th><th>Machine IP</th><th>Start Time</th><th>End Time</th><th>Status</th><th>Records Read</th></tr>");
                int idx = 1;
                foreach (var l in successLogs)
                {
                    machineNameMap.TryGetValue(l.Machine_IP, out var name);
                    sb.Append($@"<tr>
                        <td>{idx++}</td>
                        <td><strong>{Encode(name ?? "Unknown")}</strong></td>
                        <td>{Encode(l.Machine_IP)}</td>
                        <td>{l.Connection_StartTime:dd-MMM-yyyy HH:mm:ss}</td>
                        <td>{(l.Connection_EndTime.HasValue ? l.Connection_EndTime.Value.ToString("dd-MMM-yyyy HH:mm:ss") : "—")}</td>
                        <td><span class='badge badge-success'>{Encode(l.Status)}</span></td>
                        <td><strong>{l.RecordsRead?.ToString() ?? "0"}</strong></td>
                    </tr>");
                }
                sb.Append("</table>");
            }

            // ── No logs at all ────────────────────────────────────────────
            if (!logs.Any() && !neverConnected.Any())
            {
                sb.Append("<p style='text-align:center; color:#64748b; padding:20px;'>No connection logs found for today.</p>");
            }

            // ── Summary ───────────────────────────────────────────────────
            sb.Append($@"
        <div style='background:#f0f4fa; border-radius:8px; padding:14px 18px; margin-top:16px; font-size:13px; color:#475569;'>
            <strong>Summary:</strong>
            Total Logs: {logs.Count} &bull;
            Successful: {successLogs.Count} &bull;
            Failed: {failedLogs.Count} &bull;
            Total Records Fetched: {logs.Sum(l => l.RecordsRead ?? 0):N0}
        </div>
    </div>
    <div class='footer'>
        This is an automated report from <strong>Service Hub</strong>. Do not reply to this email.
    </div>
</div>
</body>
</html>");

            // ── Send email ────────────────────────────────────────────────
            var subject = $"Machine Connection Report — {today:dd MMM yyyy} | " +
                          $"{failedLogs.Select(l => l.Machine_IP).Distinct().Count()} Failed, " +
                          $"{neverConnected.Count} Offline";

            using var client = new SmtpClient(_smtp.Host, _smtp.Port)
            {
                Credentials = new NetworkCredential(_smtp.Username, _smtp.Password),
                EnableSsl = _smtp.EnableSsl
            };

            var mail = new MailMessage
            {
                From = new MailAddress(_smtp.FromEmail, _smtp.FromName),
                Subject = subject,
                Body = sb.ToString(),
                IsBodyHtml = true
            };

            foreach (var recipient in _settings.Recipients)
            {
                if (!string.IsNullOrWhiteSpace(recipient))
                    mail.To.Add(recipient.Trim());
            }

            await client.SendMailAsync(mail, ct);
            _logger.LogInformation("Daily machine report sent to {Recipients} — {Failed} failed, {Offline} offline",
                string.Join(", ", _settings.Recipients),
                failedLogs.Select(l => l.Machine_IP).Distinct().Count(),
                neverConnected.Count);
        }

        private static string Encode(string? text) =>
            System.Net.WebUtility.HtmlEncode(text ?? "");
    }

    /// <summary>
    /// Configuration for the daily machine report email.
    /// </summary>
    public class DailyReportSettings
    {
        /// <summary>Time of day to send (24h format, e.g. "14:00").</summary>
        public string SendTime { get; set; } = "14:00";

        /// <summary>Email addresses to receive the report.</summary>
        public string[] Recipients { get; set; } = Array.Empty<string>();
    }
}

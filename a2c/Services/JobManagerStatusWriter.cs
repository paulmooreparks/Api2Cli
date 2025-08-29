using ParksComputing.Api2Cli.Runtime.Services.Jobs;
using ParksComputing.Api2Cli.Cli.Services;

namespace ParksComputing.Api2Cli.Cli.Services;

public class JobManagerStatusWriter {
    private readonly IJobManager _jobs;
    private readonly IConsoleWriter _console;
    public JobManagerStatusWriter(IJobManager jobs, IConsoleWriter console) { _jobs = jobs; _console = console; }

    public void Wire() {
        _jobs.JobQueued += (_, j) => _console.WriteLineKey("jobs.queued", "cli.jobs", code: "jobs.queued", ctx: new Dictionary<string, object?>{ ["id"] = j.Id, ["name"] = j.Name, ["kind"] = j.Kind});
        _jobs.JobStarted += (_, j) => _console.WriteLineKey("jobs.started", "cli.jobs", code: "jobs.started", ctx: new Dictionary<string, object?>{ ["id"] = j.Id, ["name"] = j.Name, ["kind"] = j.Kind});
        _jobs.JobCompleted += (_, j) => _console.WriteLineKey("jobs.completed", "cli.jobs", code: "jobs.completed", ctx: new Dictionary<string, object?>{ ["id"] = j.Id, ["name"] = j.Name, ["kind"] = j.Kind});
        _jobs.JobFailed += (_, t) => _console.WriteErrorKey("jobs.failed", "cli.jobs", code: "jobs.failed", ctx: new Dictionary<string, object?>{ ["id"] = t.job.Id, ["name"] = t.job.Name, ["kind"] = t.job.Kind, ["error"] = t.ex.Message});
    }
}

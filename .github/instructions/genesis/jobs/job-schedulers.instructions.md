---
applyTo: "**/*JobScheduler.cs"
---

# Job Scheduler Rules

A `*JobScheduler` is a dedicated class that encapsulates the logic for scheduling a specific Quartz job. It knows which job type to schedule, how to build its data map, and how to construct the trigger.

## Interface

Every `*JobScheduler` MUST have a corresponding `I*JobScheduler` interface:

```csharp
public interface IOrderReminderJobScheduler
{
    Task<TriedEx<OneShotJobScheduleResult>> TryScheduleAsync(
        UserId ownerUserId,
        OrderId orderId,
        DateTimeOffset targetDateTime,
        CancellationToken cancellationToken);
}
```

Carter modules, unit-of-works, and other callers inject the interface — never the concrete class.

## Implementation

```csharp
internal sealed partial class OrderReminderJobScheduler(
    ILogger<OrderReminderJobScheduler> _logger,
    OneShotJobScheduler _oneShotJobScheduler) :
    IOrderReminderJobScheduler
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Scheduling order {OrderId} at {TargetDateTime}")]
    private partial void LogScheduling(OrderId orderId, DateTimeOffset targetDateTime);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Scheduled order {OrderId} with execution ID {ExecutionId}")]
    private partial void LogScheduled(OrderId orderId, JobExecutionId executionId);

    public async Task<TriedEx<OneShotJobScheduleResult>> TryScheduleAsync(
        UserId ownerUserId,
        OrderId orderId,
        DateTimeOffset targetDateTime,
        CancellationToken cancellationToken) => await
    Try.GetAsync<OneShotJobScheduleResult>(_logger, async () =>
    {
        LogScheduling(orderId, targetDateTime);

        var jobData = new JobDataMap
        {
            [OrderReminderJob.OwnerUserIdKey] = ownerUserId.Value,
            [OrderReminderJob.OrderIdKey] = orderId.Value
        };

        var trigger = TriggerBuilder
            .Create()
            .StartAt(targetDateTime)
            .WithIdentity($"Order_{orderId.Value}_{Guid.NewGuid()}", "Orders")
            .Build();

        var scheduleResult = await _oneShotJobScheduler
            .ScheduleJobAsync<OrderReminderJob>(ownerUserId, trigger, jobData, cancellationToken);

        if (!scheduleResult.Success)
        {
            return scheduleResult.Error;
        }

        LogScheduled(orderId, scheduleResult.Value.JobExecutionId);
        return new TriedEx<OneShotJobScheduleResult>(scheduleResult.Value);
    });
}
```

## Rules

### Thin orchestrators

The scheduler's only job is to build the data map, construct the trigger, and call `OneShotJobScheduler`. No business logic. No repository calls. No `UnitOfWork` calls.

### Job data map keys

Always use `const` fields defined on the job class for data map keys — never inline string literals:

```csharp
// In OrderReminderJob.cs:
internal const string OrderIdKey = "OrderId";

// In OrderReminderJobScheduler.cs:
[OrderReminderJob.OrderIdKey] = orderId.Value,
```

### Auto-discovery

`*JobScheduler` classes are auto-discovered and registered as singletons by Needlr. Do NOT manually register them in a plugin's `Configure()`.

### Logging

Use `[LoggerMessage]` source-generated logging — never `_logger.LogInformation("...", params)` directly.

### Placement

The scheduler lives in the **same vertical slice folder** as the job it schedules. If the job is `Scheduling/OrderReminderJob.cs`, the scheduler is `Scheduling/OrderReminderJobScheduler.cs`.

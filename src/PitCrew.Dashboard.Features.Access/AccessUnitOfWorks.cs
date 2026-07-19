using PitCrew.Dashboard.Features.Access.Abstractions;

namespace PitCrew.Dashboard.Features.Access;

internal interface IGetDashboardSessionUnitOfWork
{
  Task<DashboardSession?> GetOrNullAsync(
      System.Security.Claims.ClaimsPrincipal principal,
      CancellationToken cancellationToken);
}

internal sealed class GetDashboardSessionUnitOfWork(
    AccessContextService _accessContextService,
    IAccessStore _accessStore) : IGetDashboardSessionUnitOfWork
{
  public async Task<DashboardSession?> GetOrNullAsync(
      System.Security.Claims.ClaimsPrincipal principal,
      CancellationToken cancellationToken)
  {
    var accessContext = await _accessContextService.GetOrNullAsync(
        principal,
        cancellationToken);
    return accessContext is null
        ? null
        : await _accessStore.GetSessionAsync(
            accessContext.User,
            accessContext.IsSystemAdministrator,
            cancellationToken);
  }
}

internal interface ICreateTenantUnitOfWork
{
  Task<AccessMutationStatus> CreateAsync(
      System.Security.Claims.ClaimsPrincipal principal,
      string tenantId,
      string displayName,
      CancellationToken cancellationToken);
}

internal sealed class CreateTenantUnitOfWork(
    AccessContextService _accessContextService,
    IAccessStore _accessStore,
    TimeProvider _timeProvider) : ICreateTenantUnitOfWork
{
  public async Task<AccessMutationStatus> CreateAsync(
      System.Security.Claims.ClaimsPrincipal principal,
      string tenantId,
      string displayName,
      CancellationToken cancellationToken)
  {
    var accessContext = await _accessContextService.GetOrNullAsync(
        principal,
        cancellationToken);
    if (accessContext?.IsSystemAdministrator != true)
    {
      return AccessMutationStatus.NotFound;
    }
    return await _accessStore.CreateTenantAsync(
        tenantId,
        displayName,
        accessContext.User.GitHubUserId,
        _timeProvider.GetUtcNow(),
        cancellationToken);
  }
}

internal interface IRenameTenantUnitOfWork
{
  Task<AccessMutationStatus> RenameAsync(
      string tenantId,
      string displayName,
      CancellationToken cancellationToken);
}

internal sealed class RenameTenantUnitOfWork(
    IAccessStore _accessStore) : IRenameTenantUnitOfWork
{
  public Task<AccessMutationStatus> RenameAsync(
      string tenantId,
      string displayName,
      CancellationToken cancellationToken) =>
      _accessStore.RenameTenantAsync(
          tenantId,
          displayName,
          cancellationToken);
}

internal interface IGetTenantMembershipsUnitOfWork
{
  Task<IReadOnlyList<TenantMember>> GetMembersAsync(
      string tenantId,
      CancellationToken cancellationToken);

  Task<IReadOnlyList<DashboardUser>> GetAvailableUsersAsync(
      string tenantId,
      CancellationToken cancellationToken);
}

internal sealed class GetTenantMembershipsUnitOfWork(
    IAccessStore _accessStore) : IGetTenantMembershipsUnitOfWork
{
  public Task<IReadOnlyList<TenantMember>> GetMembersAsync(
      string tenantId,
      CancellationToken cancellationToken) =>
      _accessStore.GetMembersAsync(
          tenantId,
          cancellationToken);

  public Task<IReadOnlyList<DashboardUser>> GetAvailableUsersAsync(
      string tenantId,
      CancellationToken cancellationToken) =>
      _accessStore.GetAvailableUsersAsync(
          tenantId,
          cancellationToken);
}

internal interface ISetTenantMembershipUnitOfWork
{
  Task<AccessMutationStatus> SetAsync(
      System.Security.Claims.ClaimsPrincipal principal,
      string tenantId,
      string githubUserId,
      TenantRole role,
      CancellationToken cancellationToken);
}

internal sealed class SetTenantMembershipUnitOfWork(
    AccessContextService _accessContextService,
    IAccessStore _accessStore,
    TimeProvider _timeProvider) : ISetTenantMembershipUnitOfWork
{
  public async Task<AccessMutationStatus> SetAsync(
      System.Security.Claims.ClaimsPrincipal principal,
      string tenantId,
      string githubUserId,
      TenantRole role,
      CancellationToken cancellationToken)
  {
    var accessContext = await _accessContextService.GetOrNullAsync(
        principal,
        cancellationToken);
    return accessContext is null
        ? AccessMutationStatus.NotFound
        : await _accessStore.SetMembershipAsync(
            tenantId,
            githubUserId,
            role,
            accessContext.User.GitHubUserId,
            _timeProvider.GetUtcNow(),
            cancellationToken);
  }
}

internal interface IRemoveTenantMembershipUnitOfWork
{
  Task<AccessMutationStatus> RemoveAsync(
      string tenantId,
      string githubUserId,
      CancellationToken cancellationToken);
}

internal sealed class RemoveTenantMembershipUnitOfWork(
    IAccessStore _accessStore) : IRemoveTenantMembershipUnitOfWork
{
  public Task<AccessMutationStatus> RemoveAsync(
      string tenantId,
      string githubUserId,
      CancellationToken cancellationToken) =>
      _accessStore.RemoveMembershipAsync(
          tenantId,
          githubUserId,
          cancellationToken);
}

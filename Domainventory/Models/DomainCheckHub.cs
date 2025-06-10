namespace Domainventory.Models
{
    using Microsoft.AspNetCore.SignalR;

    public class DomainHub : Hub
    {
		public override Task OnDisconnectedAsync(Exception? exception)
		{
			ConnectionMapping.RemoveByConnectionId(Context.ConnectionId);
			return base.OnDisconnectedAsync(exception);
		}

		public Task RegisterClient(string clientSessionId)
		{
			ConnectionMapping.AddOrUpdate(clientSessionId, Context.ConnectionId);
			return Task.CompletedTask;
		}

	}

}

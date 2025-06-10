using Domainventory.Models;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.Configure<KestrelServerOptions>(options =>
{
	options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
	options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddSignalR(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(30); // default is 15s
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(5); // default is 30s
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapHub<DomainHub>("/domainHub");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Domainventory}/{action=Index}/{id?}");

app.Run();

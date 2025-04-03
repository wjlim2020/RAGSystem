using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddLogging();
builder.Services.AddHttpClient();


// Add HttpClient configuration for Ollama
//var ollamaApiUrl = builder.Configuration["OllamaApi:BaseUrl"];
//if (string.IsNullOrEmpty(ollamaApiUrl))
//{
//    throw new InvalidOperationException("OllamaApi:BaseUrl is not configured in appsettings.json.");
//}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
//builder.Services.AddHttpClient<IOllamaService, OllamaService>();


builder.Services.AddHttpClient<IOllamaService, OllamaService>(client =>
{
    client.BaseAddress = new Uri("http://localhost:11434/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Services.AddSwaggerGen(c =>
{
    c.OperationFilter<SwaggerFileUploadFilter>();
});

builder.Services.AddScoped<IOllamaService, OllamaService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});



var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.UseCors("AllowAll");
app.UseStaticFiles(); // ✅ 讓前端可以讀取 MP3

app.Run();
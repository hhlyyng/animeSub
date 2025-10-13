var builder = WebApplication.CreateBuilder(args);

// 添加控制器服务
builder.Services.AddControllers();

// 添加CORS支持（允许前端访问）
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000") // Vite和CRA默认端口
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// 使用CORS
app.UseCors("AllowFrontend");

// 映射控制器路由
app.MapControllers();

// 添加健康检查端点
app.MapGet("/", () => new
{
    status = "running",
    timestamp = DateTime.UtcNow,
    endpoints = new[]
    {
        "GET /api/anime/today - 获取今日动漫列表"
    }
});

app.Run();
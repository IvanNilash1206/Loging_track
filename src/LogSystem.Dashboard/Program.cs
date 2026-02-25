using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using LogSystem.Dashboard.Data;

var builder = WebApplication.CreateBuilder(args);

// ─── Firebase / Firestore ───
var firebaseCredPath = builder.Configuration["Firebase:CredentialPath"]
    ?? "firebase-service-account.json";
var firebaseProjectId = builder.Configuration["Firebase:ProjectId"]
    ?? throw new InvalidOperationException("Firebase:ProjectId is required in configuration.");

// Initialize Firebase Admin SDK (used for auth / optional features)
if (FirebaseApp.DefaultInstance == null)
{
    FirebaseApp.Create(new AppOptions
    {
        Credential = GoogleCredential.FromFile(firebaseCredPath),
        ProjectId = firebaseProjectId
    });
}

// Initialize Firestore client
var firestoreDb = new FirestoreDbBuilder
{
    ProjectId = firebaseProjectId,
    CredentialsPath = firebaseCredPath
}.Build();

builder.Services.AddSingleton(firestoreDb);
builder.Services.AddSingleton<FirestoreService>();

// Controllers
builder.Services.AddControllers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "LogSystem Dashboard API", Version = "v1" });
});

// CORS — allow dashboard frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("Dashboard", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Verify Firestore connection on startup
try
{
    var fs = app.Services.GetRequiredService<FirestoreDb>();
    app.Logger.LogInformation("Connected to Firestore project: {ProjectId}", fs.ProjectId);
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Failed to connect to Firestore. Check Firebase:CredentialPath and Firebase:ProjectId.");
    throw;
}

// Middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("Dashboard");
app.UseAuthorization();
app.MapControllers();

// Serve static files for the dashboard UI
app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();



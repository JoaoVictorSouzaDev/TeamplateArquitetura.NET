using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using AppProject.Core.API.Auth;
using AppProject.Core.API.Middlewares;
using AppProject.Core.Contracs;
using AppProject.Core.Infra.Database;
using AppProject.Core.Infra.Database.Entities.Auth;
using AppProject.Core.Infra.Database.Mapper;
using AppProject.Core.Services;
using AppProject.Exceptions;
using Mapster;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using Microsoft.OpenApi;

namespace AppProject.Core.API.Bootstraps;

public static class Bootstrap
{
    public static WebApplicationBuilder AddApiServices(this WebApplicationBuilder builder)
    {
        var mvcBuilder = builder.Services.AddControllers();

        ConfigureControllers(mvcBuilder);
        ConfigureLocalization(builder, mvcBuilder);

        builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            ConfigureValidations(options);
        });

        ConfigureService(builder);

        ConfigureUsers(builder);

        ConfigureMapper(builder);

        ConfigureDatabase(builder);

        ConfigureAuthentication(builder);

        ConfigureSwagger(builder);

        return builder;
    }

    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.UseRequestLocalization();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();

            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1");

                var auth0Options = new Auth0Options();
                app.Configuration.GetSection("Auth0").Bind(auth0Options);

                c.OAuthClientId(auth0Options.ClientId);
                c.OAuthAppName("API - Swagger");
                c.OAuthUsePkce();
                c.OAuthScopeSeparator(" ");

                c.OAuthScopes("openid", "profile", "email", "offline_access");

                c.OAuthAdditionalQueryStringParams(new Dictionary<string, string>
                {
                    { "audience", auth0Options.Audience ?? string.Empty }
                });
            });
        }

        app.UseMiddleware<ExceptionMiddleware>();

        app.UseHttpsRedirection();

        app.UseAuthentication();

        app.UseAuthorization();

        app.MapControllers();

        return app;
    }

    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var applicationDbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await applicationDbContext.Database.MigrateAsync();
    }

    public static async Task CreateorUpdateSystemAdminAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateAsyncScope();
        var applicationDbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var systemAdminUserOptions = new SystemAdminUserOptions();
        app.Configuration.GetSection("SystemAdminUser").Bind(systemAdminUserOptions);

        if (string.IsNullOrWhiteSpace(systemAdminUserOptions.Name) || string.IsNullOrWhiteSpace(systemAdminUserOptions.Email))
        {
            throw new ArgumentException("SystemAdminUser configuration is not set properly.");
        }

        var user = await applicationDbContext.Users.FirstOrDefaultAsync(u => u.IsSystemAdmin);

        if (user == null)
        {
            var adminUserID = Guid.NewGuid();

            user = new TbUser
            {
                Id = adminUserID,
                Name = systemAdminUserOptions.Name,
                Email = systemAdminUserOptions.Email,
                IsSystemAdmin = true,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = adminUserID,
                CreatedByUserName = systemAdminUserOptions.Name!
            };

            applicationDbContext.Users.Add(user);
            await applicationDbContext.SaveChangesAsync();
        }
        else if (user.Name != systemAdminUserOptions.Name || user.Email != systemAdminUserOptions.Email)
        {
            user.Name = systemAdminUserOptions.Name!;
            user.Email = systemAdminUserOptions.Email!;
            user.UpdatedAt = DateTime.UtcNow;
            user.UpdatedByUserId = user.Id;
            user.UpdatedByUserName = user.Name;

            applicationDbContext.Users.Update(user);
            await applicationDbContext.SaveChangesAsync();
        }
    }

    private static void ConfigureControllers(IMvcBuilder mvcBuilder)
    {
        foreach (var assembly in GetControllerAssemblies())
        {
            mvcBuilder.AddApplicationPart(assembly);
        }
    }

    private static void ConfigureLocalization(WebApplicationBuilder builder, IMvcBuilder mvcBuilder)
    {
        mvcBuilder.AddDataAnnotationsLocalization();

        builder.Services.AddLocalization();

        builder.Services.Configure<RequestLocalizationOptions>(options =>
        {
            var supportedCultures = new[] { "en-US", "pt-BR", "es-Es" };
            options.DefaultRequestCulture = new RequestCulture("en-US");
            options.SupportedCultures = supportedCultures.Select(c => new CultureInfo(c)).ToList();
            options.SupportedUICultures = supportedCultures.Select(c => new CultureInfo(c)).ToList();
            options.RequestCultureProviders = new List<IRequestCultureProvider>
            {
                new QueryStringRequestCultureProvider(),
                new CookieRequestCultureProvider(),
                new AcceptLanguageHeaderRequestCultureProvider()
            };
        });
    }

    private static void ConfigureValidations(ApiBehaviorOptions options)
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var modelErrors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .SelectMany(e => e.Value!.Errors.Select(er => er.ErrorMessage));
            var errors = modelErrors.Any() ? string.Join(" ", modelErrors) : null;
            throw new AppException(ExceptionCode.RequestValidation, errors);
        };
    }

    private static void ConfigureService(WebApplicationBuilder builder)
    {
        builder.Services.Scan(x =>
            x.FromAssemblies(GetServiceAssemblies())
            .AddClasses(y =>
                y.AssignableTo<ITransientService>())
            .AsImplementedInterfaces()
            .WithTransientLifetime());

        builder.Services.Scan(x =>
            x.FromAssemblies(GetServiceAssemblies())
            .AddClasses(y =>
                y.AssignableTo<IScopedService>())
            .AsImplementedInterfaces()
            .WithTransientLifetime());

        builder.Services.Scan(x =>
            x.FromAssemblies(GetServiceAssemblies())
            .AddClasses(y =>
                y.AssignableTo<ISingletonService>())
            .AsImplementedInterfaces()
            .WithTransientLifetime());
    }

    private static void ConfigureUsers(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IUserContext, UserContext>();

        builder.Services.AddHttpContextAccessor();
    }

    private static void ConfigureMapper(WebApplicationBuilder builder)
    {
        builder.Services.AddMapster();

        builder.Services.Scan(scan => scan
        .FromAssemblyOf<IRegisterMapsterConfig>()
        .AddClasses(classes => classes.AssignableTo<IRegisterMapsterConfig>())
        .As<IRegisterMapsterConfig>()
        .WithSingletonLifetime());

        var provider = builder.Services.BuildServiceProvider();
        var configs = provider.GetServices<IRegisterMapsterConfig>();

        var config = TypeAdapterConfig.GlobalSettings;

        foreach (var mapConfig in configs)
        {
            mapConfig.Register(config);
        }

        builder.Services.AddSingleton(config);
    }

    private static void ConfigureDatabase(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IDatabaseRepository, DatabaseRepository>();

        var connectionSringsOptions = new ConnectionSringsOptions();
        builder.Configuration.GetSection("ConnectionStrings").Bind(connectionSringsOptions);

        var databaseCoonection = connectionSringsOptions.DatabaseConnection;
        if (string.IsNullOrWhiteSpace(databaseCoonection))
        {
            throw new ArgumentException("Database connection string is not configured.");
        }

        builder.Services.AddDbContext<ApplicationDbContext>(x =>
            x.UseSqlServer(
                databaseCoonection,
                y => y.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
    }

    private static void ConfigureAuthentication(WebApplicationBuilder builder)
    {
        builder.Services.AddAuthorization();
        var auth0Options = new Auth0Options();
        builder.Configuration.GetSection("Auth0").Bind(auth0Options);

        var authority = auth0Options.Authority;
        var audience = auth0Options.Audience;

        if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(audience))
        {
            throw new ArgumentException("Auth0 configuration is not set properly.");
        }

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.Audience = audience;

            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = authority,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                NameClaimType = ClaimTypes.NameIdentifier
            };
        });
    }

    private static void ConfigureSwagger(WebApplicationBuilder builder)
    {
        var auth0Options = new Auth0Options();
        builder.Configuration.GetSection("Auth0").Bind(auth0Options);

        var authority = auth0Options.Authority;

        if (string.IsNullOrWhiteSpace(authority))
        {
            throw new ArgumentException("Auth0 configuration is not set properly.");
        }

        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "API",
                Version = "v1"
            });

            c.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri($"{auth0Options.Authority}/authorize?prompt=login"),
                        TokenUrl = new Uri($"{auth0Options.Authority}/oauth/token"),
                        Scopes = new Dictionary<string, string>
                        {
                            { "openid", "OpenID" },
                            { "profile", "Profile" },
                            { "email", "Email" },
                            { "offline_access", "Offline Access" }
                        }
                    }
                },
                In = ParameterLocation.Header,
                Name = "Authorization",
                Scheme = "Bearer",
                BearerFormat = "JWT",
                Description = "OAuth2 with Auth0"
            });
            c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("oauth2", document)] = new List<string>
                {
                    "openid", "profile", "email", "offline_access"
                }
            });
        });
    }

    private static IEnumerable<Assembly> GetControllerAssemblies() =>
    [
        Assembly.Load("AppProject.Core.Controllers.General"),
    ];

    private static IEnumerable<Assembly> GetServiceAssemblies() =>
        [
            Assembly.Load("AppProject.Core.Services"),
            Assembly.Load("AppProject.Core.Serv.General")
        ];

    private class ConnectionSringsOptions
    {
        public string? DatabaseConnection { get; set; }
    }

    private class Auth0Options
    {
        public string? Authority { get; set; }

        public string? ClientId { get; set; }

        public string? Audience { get; set; }
    }

    private class SystemAdminUserOptions
    {
        public string? Name { get; set; }

        public string? Email { get; set; }
    }
}

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using IkeMtz.NRSRx.Core.Web;
using Microsoft.AspNet.OData.Builder;
using Microsoft.AspNet.OData.Extensions;
using Microsoft.AspNet.OData.Formatter;
using Microsoft.AspNet.OData.Formatter.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Microsoft.OData;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace IkeMtz.NRSRx.Core.OData
{
  public abstract class CoreODataStartup : CoreWebStartup
  {
    protected CoreODataStartup(IConfiguration configuration) : base(configuration)
    {
    }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
      SetupLogging(services);
      SetupDatabase(services, Configuration.GetValue<string>("DbConnectionString"));
      SetupAuthentication(SetupJwtAuthSchema(services));
      SetupMiscDependencies(services);
      _ = SetupCoreEndpointFunctionality(services)
          .AddApplicationPart(StartupAssembly);
      SetupSwagger(services);
    }

    [SuppressMessage("Design", "CA1062:Validate arguments of public methods",
      Justification = "VersionedODataModelBuilder is provided by the OData API version library.")]
    public virtual void Configure(IApplicationBuilder app, IWebHostEnvironment env, VersionedODataModelBuilder modelBuilder, IApiVersionDescriptionProvider provider)
    {
      if (env.IsDevelopment())
      {
        _ = app.UseDeveloperExceptionPage();
      }
      else
      {
        _ = app.UseHsts();
      }

      _ = app.UseAuthentication()
          .UseAuthorization();
      _ = app
          .UseMvc(routeBuilder =>
          {
            var models = modelBuilder.GetEdmModels().ToList();
            var singleton = Microsoft.OData.ServiceLifetime.Singleton;
            _ = routeBuilder
            .SetTimeZoneInfo(TimeZoneInfo.Utc)
            .Select()
            .Expand()
            .OrderBy()
            .MaxTop(100)
            .Filter()
            .Count()
            .MapVersionedODataRoutes("odata-bypath", "odata/v{version:apiVersion}", models, oBuilder =>
            {
              _ = oBuilder.AddService<ODataSerializerProvider, NrsrxODataSerializerProvider>(singleton);
            });
          })
          .UseSwagger()
          .UseSwaggerUI(options =>
            {
              foreach (var description in provider.ApiVersionDescriptions)
              {
                options.SwaggerEndpoint($"./swagger/{description.GroupName}/swagger.json", description.GroupName.ToUpperInvariant());
              }
              options.EnableDeepLinking();
              options.EnableFilter();
              options.RoutePrefix = string.Empty;
              options.HeadContent += "<meta name=\"robots\" content=\"none\" />";
              options.OAuthClientId(Configuration.GetValue<string>("SwaggerClientId"));
              options.OAuthAppName(Configuration.GetValue<string>("SwaggerAppName"));
            });
    }

    public IMvcBuilder SetupCoreEndpointFunctionality(IServiceCollection services)
    {
      var mvcBuilder = services
           .AddMvc(options =>
           {
             options.EnableEndpointRouting = false;
             options.FormatterMappings.SetMediaTypeMappingForFormat("xml", MediaTypeHeaderValue.Parse("application/xml"));
             foreach (var outputFormatter in options.OutputFormatters.OfType<ODataOutputFormatter>().Where(_ => _.SupportedMediaTypes.Count == 0))
             {
               outputFormatter.SupportedMediaTypes.Add(new MediaTypeHeaderValue("application/prs.odatatestxx-odata"));
             }
             foreach (var inputFormatter in options.InputFormatters.OfType<ODataInputFormatter>().Where(_ => _.SupportedMediaTypes.Count == 0))
             {
               inputFormatter.SupportedMediaTypes.Add(new MediaTypeHeaderValue("application/prs.odatatestxx-odata"));
             }
             SetupMvcOptions(services, options);
           });
      _ = services.AddApiVersioning(options => options.ReportApiVersions = true)
          .AddOData()
          .EnableApiVersioning();
      _ = services.AddODataApiExplorer(
          options =>
          {
            // add the versioned api explorer, which also adds IApiVersionDescriptionProvider service
            // note: the specified format code will format the version as "'v'major[.minor][-status]"
            options.GroupNameFormat = "'v'VVV";

            // note: this option is only necessary when versioning by url segment. the SubstitutionFormat
            // can also be used to control the format of the API version in route templates
            options.SubstituteApiVersionInUrl = true;
          });
      return mvcBuilder;
    }

    public void SetupSwagger(IServiceCollection services)
    {
      _ = services
        .AddTransient<IConfigureOptions<SwaggerGenOptions>>(serviceProvider => new ConfigureSwaggerOptions(serviceProvider.GetRequiredService<IApiVersionDescriptionProvider>(), this))
        .AddSwaggerGen(options =>
        {
          options.OperationFilter<ODataCommonOperationFilter>();
          options.UseInlineDefinitionsForEnums();
          // add a custom operation filter which sets default values
          options.OperationFilter<SwaggerDefaultValues>();
          var audiences = GetIdentityAudiences();
          var swaggerIdentityProviderUrl = Configuration.GetValue<string>("SwaggerIdentityProviderUrl");
          if (audiences.Any() && !string.IsNullOrWhiteSpace(swaggerIdentityProviderUrl))
          {
            var audience = audiences.FirstOrDefault();

            options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
            {
              Type = SecuritySchemeType.OAuth2,
              In = ParameterLocation.Header,
              Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
              Scheme = JwtBearerDefaults.AuthenticationScheme,
              Flows = new OpenApiOAuthFlows
              {
                Implicit = new OpenApiOAuthFlow
                {
                  AuthorizationUrl = new Uri($"{swaggerIdentityProviderUrl}authorize?audience={audience}"),
                  Scopes = SwaggerScopes,
                },
              }
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = JwtBearerDefaults.AuthenticationScheme}
                        },
                       Array.Empty<string>()
                    }
                });
          }
        });
    }
  }
}

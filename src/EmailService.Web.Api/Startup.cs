﻿using EmailService.Core;
using EmailService.Core.Entities;
using EmailService.Core.Services;
using EmailService.Web.Api.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.PlatformAbstractions;
using Swashbuckle.Swagger.Model;
using System.IO;
using static EmailService.Core.Constants;

namespace EmailService.Web.Api
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();
            HostingEnv = env;
        }

        public IConfigurationRoot Configuration { get; }

        public IHostingEnvironment HostingEnv { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMemoryCache();

            // set up our EF data context
            var connection = Configuration.GetConnectionString(ConnectionStrings.SqlServer);
            if (!string.IsNullOrEmpty(connection))
            {
                services.AddDbContext<EmailServiceContext>(options =>
                {
                    options.UseSqlServer(connection, b => b.MigrationsAssembly("EmailService.Web"));
                    options.UseMemoryCache(null);
                });
            }

            // add custom services
            services.AddTransient<IApplicationKeyStore, DbApplicationKeyStore>();
            services.AddSingleton<ICryptoServices>(RsaCryptoServices.Instance);
            services.AddAzureStorageServices(options =>
            {
                options.ConnectionString = Configuration.GetConnectionString(ConnectionStrings.Storage);
            });

            // configure Kestrel HTTPS
            var useSsl = Configuration["LOCAL_CERT_FILE"] != null;
            if (useSsl)
            {
                services.Configure<KestrelServerOptions>(options =>
                {
                    if (HostingEnv.IsDevelopment())
                    {
                        var certFile = Configuration["LOCAL_CERT_FILE"];
                        var certPass = Configuration["LOCAL_CERT_PASS"];
                        options.UseHttps(certFile, certPass);
                    }
                });
            }

            // Add framework services.
            services.AddMvc(options =>
            {
                if (useSsl)
                {
                    if (HostingEnv.IsDevelopment())
                    {
                        options.SslPort = 44321;
                    }

                    options.Filters.Add(new RequireHttpsAttribute());
                }
            });

            // set up basic authentication options
            services.Configure<BasicAuthenticationOptions>(options =>
            {
                options.AutomaticAuthenticate = true;
                options.Realm = "SBS Email Service";
            });

            // Add auto-documentation services
            services.AddSwaggerGen(options =>
            {
                // TODO: get this from config if possible
                options.SingleApiVersion(new Info
                {
                    Version = "v1",
                    Title = "Sun Branding Solutions Common Email API",
                    Description = "A centralised API for sending and templating emails for SBS applications.",
                    Contact = new Contact
                    {
                        Email = "kwilliams@sunbrandingsolutions.com",
                        Name = "Keith Williams"
                    }
                });
                
                options.AddSecurityDefinition("basic", new BasicAuthScheme());

                var xmlPath = GetXmlCommentsPath();
                if (File.Exists(xmlPath))
                {
                    options.IncludeXmlComments(xmlPath);
                }

                options.OperationFilter<Filters.SwaggerRemoveCancellationTokenParameterFilter>();
                options.DescribeAllEnumsAsStrings();
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            app.UseBasicAuthentication();

            app.UseMvc();

            app.UseSwagger();
            app.UseSwaggerUi("swagger/ui");
        }

        private string GetXmlCommentsPath()
        {
            var app = PlatformServices.Default.Application;
            return Path.Combine(app.ApplicationBasePath, Path.ChangeExtension(app.ApplicationName, "Api.xml"));
        }
    }
}

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Hosting;
using Microsoft.AspNet.Http;
using Microsoft.Extensions.DependencyInjection;
using TwitterClone.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.PlatformAbstractions;
using Microsoft.Extensions.Logging;
using Microsoft.AspNet.Diagnostics;
using Newtonsoft.Json.Serialization;
using AutoMapper;
using TwitterClone.ViewModels;
using Microsoft.AspNet.Identity.EntityFramework;
using Microsoft.AspNet.Mvc;
using Microsoft.AspNet.Authentication.Cookies;
using System.Net;
using TwitterClone.Models;
using SendGridMessenger;
using S3Services;

namespace TwitterClone
{
    public class Startup
    {
        public static IConfigurationRoot Configuration;

        public Startup(IApplicationEnvironment appEnv)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(appEnv.ApplicationBasePath)
                .AddJsonFile("config.json")
                .AddEnvironmentVariables();

            Configuration = builder.Build();
        }
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit http://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc(config =>
            {
//#if !DEBUG
//                config.Filters.Add(new RequireHttpsAttribute());
//#endif
            })
            .AddJsonOptions(opt =>
            {
                opt.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            });

            services.AddSignalR();

            services.AddIdentity<TwitterCloneUser, IdentityRole>(config =>
            {
                config.User.RequireUniqueEmail = true;
                config.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890-._";

                config.Password.RequiredLength = 8;
                config.Password.RequireLowercase = false;
                config.Password.RequireNonLetterOrDigit = false;
                config.Password.RequireDigit = false;
                config.Password.RequireUppercase = false;

                config.Cookies.ApplicationCookie.LoginPath = "/Login";
                config.Cookies.ApplicationCookie.Events = new CookieAuthenticationEvents()
                {
                    OnRedirectToLogin = ctx =>
                    {
                        if (ctx.Request.Path.StartsWithSegments("/api") && ctx.Response.StatusCode == (int)HttpStatusCode.OK)
                        {
                            ctx.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                        }
                        else
                        {
                            ctx.Response.Redirect(ctx.RedirectUri);
                        }
                        return Task.FromResult(0);
                    }
                };
            })
            .AddEntityFrameworkStores<TwitterCloneContext>()
            .AddDefaultTokenProviders();

            services.AddLogging();

            services.AddEntityFramework()
                .AddNpgsql()
                .AddDbContext<TwitterCloneContext>();

            services.AddTransient<TwitterCloneContextSeedData>();
            services.AddScoped<ITwitterCloneRepository, TwitterCloneRepository>();

            services.AddOptions();

            // Configure Options for SendGrid

            services.Configure<AuthMessageSenderOptions>(Configuration);
            services.Configure<AuthMessageSenderOptions>(myOptions =>
            {
                myOptions.SendGridUser = Configuration["SendGrid:SendGridUser"];
                myOptions.SendGridPassword = Configuration["SendGrid:SendGridPassword"];
                myOptions.SendGridKey = Configuration["SendGrid:SendGridKey"];
            });

            services.AddTransient<IEmailSender, AuthMessageSender>();
            services.AddTransient<ISmsSender, AuthMessageSender>();
            services.Configure<AuthMessageSenderOptions>(Configuration);

            services.Configure<S3ProfilePictureServiceOptions>(Configuration);
            services.Configure<S3ProfilePictureServiceOptions>(myOptions =>
            {
                myOptions.AWSProfileName = Configuration["S3ProfilePicture:AWSProfileName"];
                myOptions.Bucket = Configuration["S3ProfilePicture:Bucket"];
            });

            services.AddTransient<IProfilePictureService, S3ProfilePictureService>();
            services.Configure<S3ProfilePictureServiceOptions>(Configuration);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public async void Configure(IApplicationBuilder app, TwitterCloneContextSeedData seeder, ILoggerFactory loggerFactory, IHostingEnvironment env)
        {
            if (string.Equals(env.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase))
            {
                app.UseDeveloperExceptionPage();

                app.UseRuntimeInfoPage(); // default path is /runtimeinfo
            }

            loggerFactory.AddDebug(LogLevel.Information);

            app.UseStaticFiles();

            app.UseIdentity();

            Mapper.Initialize(config =>
            {
                config.CreateMap<TwitterClonePost, TwitterClonePostViewModel>().ReverseMap();
                config.CreateMap<TwitterCloneUser, TwitterCloneUserViewModel>().ReverseMap();
            });

            app.UseSignalR();

            app.UseMvc(Configure =>
            {
                Configure.MapRoute(
                    name: "Default",
                    template: "{controller}/{action}/{id?}",
                    defaults: new { controller = "App", action = "Index" }
                );
            });

            await seeder.EnsureSeedDataAsync();
        }

        // Entry point for the application.
        public static void Main(string[] args) => WebApplication.Run<Startup>(args);
    }
}

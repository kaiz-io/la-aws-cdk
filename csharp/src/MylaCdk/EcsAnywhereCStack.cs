using Amazon.CDK;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.Route53;
using Constructs;
using System.Collections.Generic;

namespace MyLACdk
{
    // Add any properties that you want to pass to your stack
    public class EcsAnywhereCStackProps : StackProps
    {
        public string ClientName { get; set; }
        public string EnvName { get; set; }
        public ICluster Cluster { get; set; }
        public DatabaseInstance Rds { get; set; }
        public string Hosted { get; set; }
        public string Region { get; set; }
        public IHostedZone Zone { get; set; }
    }

    public class EcsAnywhereCStack : Stack
    {
        public readonly ExternalService service;
        public readonly Repository repo;

        // The code that defines your CF stack goes here
        public EcsAnywhereCStack(Construct scope, string id, EcsAnywhereCStackProps props = null)
            : base(scope, id, props)
        {
            var clientName = props.ClientName;
            string clientPrefix = $"{clientName}{props.EnvName}";

            //get pre-populated certtifcate (pem format) values from secret store - something like vault could replace this later 
            //these are used to create the EA saml provider and EA IAM SSO certificates 
            //all these will be injected as environment variables into the container
            var samlPem = Amazon.CDK.AWS.SecretsManager.Secret.FromSecretCompleteArn(this, "samlpem", "arn:aws:secretsmanager:us-east-1:654654599146:secret:SAMLProviderPem-O3bP5m");
            var samlRsaKey = Amazon.CDK.AWS.SecretsManager.Secret.FromSecretCompleteArn(this, "samlkey", "arn:aws:secretsmanager:us-east-1:654654599146:secret:SamlRsaKey-D3R6c5");
            var providerlPem = Amazon.CDK.AWS.SecretsManager.Secret.FromSecretCompleteArn(this, "providerpem", "arn:aws:secretsmanager:us-east-1:654654599146:secret:EaPem-Y32PsR");
            var providerRsaKey = Amazon.CDK.AWS.SecretsManager.Secret.FromSecretCompleteArn(this, "providerKey", "arn:aws:secretsmanager:us-east-1:654654599146:secret:EaKey-HDhRJz");

            // Create task role
            // ECS task role
            var taskRole = new Role(this, $"{clientPrefix}-task-role", new RoleProps
            {
                AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
                RoleName = $"{clientPrefix}-task-role",
                Description = "Role that the web task definitions use to run the myLA web app"
            });

            taskRole.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("service-role/AmazonECSTaskExecutionRolePolicy"));
            taskRole.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("AWSXRayDaemonWriteAccess"));


            // Grant access to Create Log group and Log Stream
            taskRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Actions = new string[] {
                     "logs:CreateLogGroup",
                     "logs:CreateLogStream",
                     "logs:PutLogEvents",
                     "logs:DescribeLogStreams"
                },
                Resources = new string[] { "*" }
            }));

            var executionRolePolicy = new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Resources = new string[] { "*" },
                Actions = new string[]
                {
                    "ecr:GetAuthorizationToken",
                    "ecr:BatchCheckLayerAvailability",
                    "ecr:GetDownloadUrlForLayer",
                    "ecr:BatchGetImage",
                    "logs:CreateLogStream",
                    "logs:PutLogEvents"
                }
            });


            //get the previously autogenerated db secret
            var dbSecret = Amazon.CDK.AWS.SecretsManager.Secret.FromSecretCompleteArn(this, "db-secret", props.Rds.Secret.SecretArn);

            var repository = Repository.FromRepositoryName(this, "myla-dev", "myla-dev");
            var image = ContainerImage.FromEcrRepository(repository, "1.2");

            // Create ExternalTaskDefinition
            var taskDef = new ExternalTaskDefinition(this, $"{clientPrefix}-task-anywhere-deff", new ExternalTaskDefinitionProps
            {
                TaskRole = taskRole,
                Family = "ecs-anywhere",
                NetworkMode = NetworkMode.BRIDGE   //this should be bridge by default but just in case              
            });

            taskDef.AddToExecutionRolePolicy(executionRolePolicy);

            taskDef.AddContainer($"{clientPrefix}-anywhere-web-container", new ContainerDefinitionOptions
            {
                User = "1654", //user defined in image
                MemoryLimitMiB = 1024,
                Image = image, //use the image from the ecr for now
                ContainerName = "myla-ecs-anywhere-container",
                PortMappings = new PortMapping[]
                {
                    new PortMapping
                    {
                        ContainerPort = 8443
                    }
                },
                //these are needed for Traefik
                DockerLabels = new Dictionary<string, string>
                {
                    { "traefik.enable","true" },
                    {"traefik.http.services.ecs-anywhere.loadbalancer.server.port", "8443"},
                    { "traefik.http.routers.ecs-anywhere.entrypoints","web-secure"},
                    { "traefik.http.routers.ecs-anywhere.tls","true"},
                    { "traefik.http.routers.ecs-anywhere.service","ecs-anywhere"},
                    {"traefik.http.routers.ecs-anywhere-host.rule", "Host(`10.4.14.176`)"},
                    { "traefik.http.routers.ecs-anywhere.rule", "Host(`ecs-anywhere.my.la.gov`)"},
                    { "traefik.http.services.ecs-anywhere.loadbalancer.server.scheme","https"},
                },
                //must set this logging in /etc/ecs/ecs.config as ECS_AVAILABLE_LOGGING_DRIVERS=["json-file","awslogs"] BEFORE registration       
                //https://github.com/aws/amazon-ecs-agent/blob/master/README.md
                //https://docs.aws.amazon.com/AmazonECS/latest/developerguide/ecs-anywhere-registration.html#ecs-anywhere-registration
                //Logging = LogDriver.AwsLogs(new AwsLogDriverProps
                //{
                //    StreamPrefix = $"{clientPrefix}-anywhere-web-container"
                //}),

                //these are the secrets that will be injected into the container as environment variables
                Secrets = new Dictionary<string, Secret>
                {
                    { "DB_PASSWORD", Secret.FromSecretsManager(dbSecret, "password")},
                    { "DB_USER", Secret.FromSecretsManager(dbSecret, "username")},
                    { "AppConfiguration__SAMLProvider__Certificate__Pem",  Secret.FromSecretsManager(samlPem) },
                    { "AppConfiguration__SAMLProvider__Certificate__RSAKey",  Secret.FromSecretsManager(samlRsaKey)},
                    { "AppConfiguration__ServiceProvider__Certificate__Pem", Secret.FromSecretsManager(providerlPem) },
                    { "AppConfiguration__ServiceProvider__Certificate__RSAKey", Secret.FromSecretsManager(providerRsaKey)}
                },
                Environment = new Dictionary<string, string>
                {
                    { "ASPNETCORE_ENVIRONMENT", "Docker" },
                    {"ASPNETCORE_URLS", "https://*:8443;http://*:8080" },
                    { "ASPNETCORE_Kestrel__Certificates__Default__Password", "1234"},
                    { "ASPNETCORE_Kestrel__Certificates__Default__Path", "/usr/local/share/ca-certificates/localhost.pfx" },
                    { "DB_HOST", props.Rds.InstanceEndpoint.Hostname},
                    { "DB_PORT", props.Rds.InstanceEndpoint.Port.ToString() },
                    { "DB_NAME", "SessionCache" }
                }
            });

            //********************************************************************************
            //Run this section commented out first then
            //after external instance is registered uncomment and run it again
            var service = new ExternalService(this, $"{clientPrefix}-ecs-anywhere-service",
                    new ExternalServiceProps
                    {
                        ServiceName = "myla-ecs-anywhere-service",
                        Cluster = props.Cluster,
                        TaskDefinition = taskDef,
                        DesiredCount = 2,
                        MaxHealthyPercent = 500,
                        MinHealthyPercent = 50
                    });

            service.TaskDefinition.AddPlacementConstraint(PlacementConstraint.MemberOf("attribute:role1 == webserver"));
            this.service = service;

            //********************************************************************************


            // Create IAM Role
            var instance_iam_role = new Role(this, $"{clientPrefix}-ecs-anywhere-role", new RoleProps
            {
                RoleName = $"{clientPrefix}-ecs-anywhere-role",
                AssumedBy = new ServicePrincipal("ssm.amazonaws.com"),
                ManagedPolicies = new IManagedPolicy[]
                {
                    ManagedPolicy.FromAwsManagedPolicyName("AmazonSSMManagedInstanceCore"),
                    ManagedPolicy.FromManagedPolicyArn(this,"EcsAnywhereEC2Policy", "arn:aws:iam::aws:policy/service-role/AmazonEC2ContainerServiceforEC2Role")
                }
            });

            instance_iam_role.WithoutPolicyUpdates();

            // cloud formation stack outputs
            new CfnOutput(this, $"RegisterExternalInstance", new CfnOutputProps
            {
                Description = "Create an Systems Manager activation pair",
                ExportName = $"{props.EnvName}-serviceName",
                Value = $"aws ssm create-activation --iam-role ${instance_iam_role.RoleName} | tee ssm-activation.json"
            });

            new CfnOutput(this, "DownloadInstallationScript", new CfnOutputProps
            {
                Description = "On your VM, download installation script",
                Value = "curl --proto 'https' -o '/tmp/ecs-anywhere-install.sh' 'https://amazon-ecs-agent.s3.amazonaws.com/ecs-anywhere-install-latest.sh' && sudo chmod +x ecs-anywhere-install.sh",
                ExportName = "2-DownloadInstallationScript",
            });

            new CfnOutput(this, "ExecuteScript", new CfnOutputProps
            {
                Description = "Run installation script on VM",
                Value = "sudo ./ecs-anywhere-install.sh  --region $REGION --cluster $CLUSTER_NAME --activation-id $ACTIVATION_ID --activation-code $ACTIVATION_CODE",
                ExportName = "3-ExecuteInstallationScript"
            });
        }
    }
}

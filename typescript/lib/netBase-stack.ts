import * as cdk from "aws-cdk-lib";
import { Construct } from "constructs";
import * as secrets from "aws-cdk-lib/aws-secretsmanager";
import * as ec2 from "aws-cdk-lib/aws-ec2";
import * as ecs from "aws-cdk-lib/aws-ecs";

interface NetBaseStackProps extends cdk.StackProps {
  clientName: string;
  envName: string;
  domain: string;
  region: string;
  cidr: string;
}

export class NetBaseStack extends cdk.Stack {

  public readonly vpc: ec2.IVpc;
  public readonly clientName: string;
  public readonly envName: string;
  public readonly cluster : ecs.ICluster; 

  constructor(scope: Construct, id: string, props: NetBaseStackProps) {
    super(scope, id, props);
       
    const clientName = props.clientName;
    const clientPrefix = `${clientName}-${props.envName}`;
    const hosted = `${props.envName}.${clientName}.${props.domain}`;

    //vpc resources
    //TODO: do a lookup and see if that vpc exists
    //if not, create the dev or prod vpc
    const vpc = new ec2.Vpc(this, `${clientPrefix}-vpc`, {
      maxAzs: 2,      
      vpcName: `${clientPrefix}-vpc`,      
      ipAddresses: ec2.IpAddresses.cidr("10.13.0.0/16"),    
      enableDnsHostnames: true,  
      enableDnsSupport: true,
      subnetConfiguration: [
        {
          name: `${clientPrefix}-private-subnet`,
          subnetType: ec2.SubnetType.PRIVATE_WITH_EGRESS,
          cidrMask: 24,  
        },
        {
          name: `${clientPrefix}-public-subnet`,                    
          subnetType: ec2.SubnetType.PUBLIC,
          cidrMask: 24,
        },
      ],      
    });    

    const cluster  = new ecs.Cluster(this, `${clientPrefix}-ecs-cluster`, {
      vpc: vpc,
      clusterName: `${clientPrefix}-ecs-cluster`,    
    });     

        // const zone = new route53.PrivateHostedZone(this, `${clientPrefix}-zone`, {
    //   vpc: vpc,      
    //   zoneName: hosted,      
    //   comment: `${props.envName} sample web domain`
    // });
    
    // new route53.ARecord(this, `${clientPrefix}-domain`, {
    //   recordName: `${hosted}`,
    //   target: route53.RecordTarget.fromAlias(
    //     new route53targets.LoadBalancerTarget(elb)
    //   ),
    //   ttl: cdk.Duration.seconds(300),
    //   comment: `${props.envName} sample web domain`,
    //   region: `${props.region}`,
    //   zone: zone,
    // });

    // const cert = new cm.Certificate(
    //   this,
    //   `${clientPrefix}-cert`,
    //   {
    //     domainName: `${hosted}`,
    //     subjectAlternativeNames: [`*.${hosted}`],
    //     validation: cm.CertificateValidation.fromDns(zone),
    //   });

    //add secret to get container later from private registry
    //cdk deploy --parameters appdmApiKey=12345 --profile sandbox EcsStacks
    const appdmApiKeyName = new cdk.CfnParameter(this, "appdmApiKey", {
      type: "String",
      description: "AppDm Docker Hub Key",
      noEcho: true, //do not show in cf template
    });

    const apiKeySecret = new secrets.Secret(this, "AppDm-Docker-API-Key", {
      secretName: "APPDM_DOCKER_API_Key",
      secretStringValue: cdk.SecretValue.unsafePlainText(
        appdmApiKeyName.valueAsString
      ),
    });

    this.vpc = vpc;
    this.clientName = clientName;
    this.envName = props.envName; 
    this.cluster = cluster;       

    new cdk.CfnOutput(this, `${props.envName}-clusterName`, {
      exportName: `${props.envName}-clusterName`,
      value: cluster.clusterName,
    });
  }
}
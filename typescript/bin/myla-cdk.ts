#!/usr/bin/env node
import 'source-map-support/register';
import * as cdk from 'aws-cdk-lib';
import { EcsStack } from '../lib/ecs-stack';
import { EcsAnywhereStack } from '../lib/ecs-anywhere-stack';
import { RabbitAnywhereStack } from '../lib/rabbit-anywhere-stack';
import { RabbitStack } from "../lib/rabbit-stack";
import { StateFulStack } from '../lib/stateful-stack';
import { NetBaseStack } from '../lib/netBase-stack';
import { LoadBalancerStack } from '../lib/load-balancer-stack';

enum EnvName{
  DEV = "dev",
  PROD = "prod"
}

const env = {account: '654654599146',  region: 'us-east-1' }
const app = new cdk.App();

//creates vpc, subnets, clusters, hosted zone
const netBaseStack = new NetBaseStack(app, 'NetBaseStack', { 
  //substitute with your personal variable values here
  //in this section below
  clientName: 'dina', //agency name?
  envName: EnvName.DEV,
  hosted: "ecs.my.la.gov", //the subdomain name that will be created in Route 53
  hostedAnywhere: "ecs-anywhere.my.la.gov", //the DNS entry and subdomain created by DS Admin
  cidr: "10.13.0.0/16",
  env
});

//creates caching db
const statefulStack= new StateFulStack(app, 'StateFulStack', { 
  clientName: netBaseStack.clientName,
  envName: netBaseStack.envName,
  vpc: netBaseStack.vpc,
  cluster: netBaseStack.cluster,
  clusterAnywhere: netBaseStack.clusterAnywhere,
  env
});

new EcsStack(app, 'EcsStack', {     
    clientName: netBaseStack.clientName,
    envName: netBaseStack.envName,    
    cluster: netBaseStack.cluster,   
    rds: statefulStack.rds,
    hosted: netBaseStack.hosted,
     //previously created certificate in AWS ACM
    certificateArn: 'arn:aws:acm:us-east-1:654654599146:certificate/72fcdfb5-addf-4846-8883-07c41e6edf40',
    region: netBaseStack.region,
    zone: netBaseStack.zone,
    env
});

new RabbitStack(app, 'RStack', {     
  clientName: netBaseStack.clientName,
  envName: netBaseStack.envName,    
  cluster: netBaseStack.cluster, 
  hosted: netBaseStack.hosted,
  region: netBaseStack.region,
  zone: netBaseStack.zone,
  env
});

new EcsAnywhereStack(app, 'EcsAnywhereStack', {     
  description: "ECS Anywhere Stack",
  clientName: netBaseStack.clientName,
  envName: netBaseStack.envName,    
  cluster: netBaseStack.clusterAnywhere,
  rds: statefulStack.rds,
  hosted: netBaseStack.hosted,
  region: netBaseStack.region,  
  env
});

new RabbitAnywhereStack(app, 'RsAnywhereStack', {     
  description: "Anywhere Stack",
  clientName: netBaseStack.clientName,
  envName: netBaseStack.envName,    
  cluster: netBaseStack.clusterAnywhere, 
  hosted: netBaseStack.hosted,
  region: netBaseStack.region,  
  env
});

new LoadBalancerStack(app, 'LoadBalancerStack', {
  description: "Traefik Proxy/Load Balancer Stack",
  clientName: netBaseStack.clientName,
  envName: netBaseStack.envName,    
  cluster: netBaseStack.clusterAnywhere, 
  hostnameAnywhere:"ecs-anywhere.my.la.gov",
  region: netBaseStack.region,  
  env
});

//this will tag all the created resources
//in all these stacks with these tags
cdk.Tags.of(app).add('client', netBaseStack.clientName);
cdk.Tags.of(app).add('environemnt', netBaseStack.envName);
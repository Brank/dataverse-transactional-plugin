using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.ServiceModel;

namespace ContractTransactionalPlugin
{
    public class ContractPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            // Obtain the tracing service
            ITracingService tracingService =
            (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            // Obtain the execution context from the service provider.  
            IPluginExecutionContext context = (IPluginExecutionContext)
                serviceProvider.GetService(typeof(IPluginExecutionContext));

            //Check if its in a transaction
            if (!context.IsInTransaction)
            {
                throw new InvalidPluginExecutionException(
                    "This operation must execute inside a Dataverse transaction."
                );
            }

            // The InputParameters collection contains all the data passed in the message request.  
            if (context.PreEntityImages.Contains("PreImage") &&
                context.PreEntityImages["PreImage"] is Entity)
            {
                // Obtain the target entity from the input parameters.  
                Entity entity = (Entity)context.PreEntityImages["PreImage"];


                // Obtain the IOrganizationService instance which you will need for  
                // web service calls.  
                IOrganizationServiceFactory serviceFactory =
                    (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

                try
                {
                    // Plug-in business logic goes here.  
                    //Get the contract
                    var contract = entity.GetAttributeValue<EntityReference>("cr6fc_contract");
                    var amount = entity.GetAttributeValue<Money>("cr6fc_amount");


                    ProcessContractTransaction(service, contract.Id, amount.Value);
                }

                catch (FaultException<OrganizationServiceFault> ex)
                {
                    throw new InvalidPluginExecutionException("An error occurred in FollowUpPlugin.", ex);
                }

                catch (Exception ex)
                {
                    tracingService.Trace("FollowUpPlugin: {0}", ex.ToString());
                    throw;
                }
            }
        }

        public void ProcessContractTransaction(
    IOrganizationService service,
    Guid contractId,
    decimal amount)
        {
            // 1. Acquire contract-level lock
            // 2. Read latest contract values
            var contract = service.Retrieve(
                "cr6fc_contract",
                contractId,
                new ColumnSet(
                    "cr6fc_ceiling",
                    "cr6fc_consumption"));

            contract["cr6fc_lastprocessingtoken"] =
                Guid.NewGuid().ToString();

            service.Update(contract);

            decimal ceiling =
                contract.GetAttributeValue<Money>(
                    "cr6fc_ceiling").Value;

            decimal consumed =
                contract.GetAttributeValue<Money>(
                    "cr6fc_consumption").Value;

            // 3. Validate available budget
            decimal available = ceiling - consumed;

            if (amount > available)
            {
                throw new InvalidPluginExecutionException(
                    $"Insufficient budget. Available: {available}");
            }

            // 4. Create financial transaction
            // 5. Update contract consumption
            contract["cr6fc_consumption"] = consumed + amount;

            service.Update(contract);

            // 6. Return to originating request pipeline
        }
    }
}

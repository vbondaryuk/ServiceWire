class Rpc{
    constructor(url) {
        this.url = url;
      }

      async callRpc(data) {
        var response = await fetch(this.url, {
            method: 'POST', 
            cache: "no-cache",
            body: JSON.stringify(data)
        })
        .then(response => {
            if(!response.ok)
                return Promise.reject({ status: response.status, statusText: response.statusText });
            return response.json();
        })
        .then(response => {
            if(response.Exception)
                return Promise.reject({ status: response.ResultType, statusText: response.Exception });
            return response.Parameters[0];
        })
        .catch(error => {
            console.error('Error:', error);
        });

        var result = await response;
        
        return result;        
      }
}

class IDataContract extends Rpc {
    constructor(url){
        super(url);

        this.serviceKey = "DemoCommon.IDataContract";        
    }

    async getDecimal(input){
        var methodKey = 0;
        var sendValue = {
            ServiceKey: this.serviceKey,
            MethodKey: methodKey,
            Parameters: [
                input
            ]
        };
        var result = this.callRpc(sendValue);

        return result;
    }
}
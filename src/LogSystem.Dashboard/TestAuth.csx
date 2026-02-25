using Google.Apis.Auth.OAuth2;
var cred = GoogleCredential.FromFile(@"C:\Users\strka\Desktop\Log_System\src\LogSystem.Dashboard\firebase-service-accoun.json")
    .CreateScoped("https://www.googleapis.com/auth/datastore");
var token = await ((ITokenAccess)cred).GetAccessTokenForRequestAsync();
Console.WriteLine("Token obtained successfully! Length: " + token.Length);
Console.WriteLine("First 20 chars: " + token.Substring(0, 20) + "...");

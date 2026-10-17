export const environment = {
  production: false,
  // Everything goes through the API Gateway — the frontend never talks to
  // individual microservices directly.
  apiBaseUrl: 'http://localhost:5000/api'
};

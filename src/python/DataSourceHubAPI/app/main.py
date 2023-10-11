from fastapi import Depends, FastAPI
import uvicorn
from app.routers import resolve_request, status

app = FastAPI(
    title='FoundationaLLM DataSourceHubAPI',
    summary='API for retrieving DataSource metadata',
    description='The FoundationaLLM DataSourceHubAPI is a wrapper around DataSourceHub functionality contained in the foundationallm.core Python SDK.',
    version='0.1.0',
    openapi_url='/swagger/v1/swagger.json',
    docs_url='/swagger',
    redoc_url=None
)

app.include_router(resolve_request.router)
app.include_router(status.router)

@app.get('/')
async def root():
    return { 'message': 'FoundationaLLM DataSourceHubAPI' }

if __name__ == '__main__':
    uvicorn.run('main:app', host='0.0.0.0', port=8842, reload=True)
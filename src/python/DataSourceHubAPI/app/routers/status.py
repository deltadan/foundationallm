"""
Status API endpoint that acts as a health check for the API.
"""
import os
from fastapi import APIRouter

router = APIRouter(
    prefix='/status',
    tags=['status'],
    responses={404: {'description':'Not found'}}
)

@router.get('')
async def get_status():
    """
    Retrieves the status of the API.
    
    Returns
    -------
    JSON
        Object containing the name, instance, version, and status of the API.
    """    
    statusMessage = {
        "name": "DataSourceHubAPI",
        "instance": os.environ["HOSTNAME"],
        "version": os.environ["FOUNDATIONALLM_VERSION"],
        "Status": "ready"
    }
    return statusMessage

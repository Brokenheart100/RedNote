import type { CurrentUser } from '../../../shared/types/users'
import { gatewayFetch } from '../../utils/gateway-fetch'

export default defineEventHandler(event =>
    gatewayFetch<CurrentUser>(event, '/api/v1/users/me'))
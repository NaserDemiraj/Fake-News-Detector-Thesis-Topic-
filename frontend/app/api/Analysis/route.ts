import { NextRequest, NextResponse } from "next/server"

export const maxDuration = 180

const BACKEND = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000"

export async function POST(request: NextRequest) {
  const body = await request.json()
  const res = await fetch(`${BACKEND}/api/Analysis`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  })
  const data = await res.json()
  return NextResponse.json(data, { status: res.status })
}

export async function GET(request: NextRequest) {
  const res = await fetch(`${BACKEND}/api/Analysis/recent`)
  const data = await res.json()
  return NextResponse.json(data, { status: res.status })
}

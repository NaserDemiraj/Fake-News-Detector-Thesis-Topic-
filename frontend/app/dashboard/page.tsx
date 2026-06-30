"use client"

import { useEffect, useState } from "react"
import AnalyticsDashboard from "@/components/analytics-dashboard"
import DatabaseViewer from "@/components/database-viewer"
import { Button } from "@/components/ui/button"
import { RefreshCw } from "lucide-react"

// Shape the dashboard component expects
interface DashboardAnalysis {
  id: string
  content: string
  type: "url" | "text"
  result: { verdict: string; score: number }
  timestamp: number
  isFavorite: boolean
}

// Raw record returned by the backend (camelCase SavedAnalysis)
interface RecentRecord {
  id: string
  title: string
  url: string
  contentType: string
  content: string
  score: number
  verdict: string
  date: string
  isFavorite: boolean
}

export default function DashboardPage() {
  const [analyses, setAnalyses] = useState<DashboardAnalysis[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [refreshTrigger, setRefreshTrigger] = useState(0)

  const load = async () => {
    try {
      setLoading(true)
      setError(null)
      // The /recent endpoint is [Authorize]-protected; send the JWT the static
      // pages store in localStorage (key 'auth_token'), matching their authFetch.
      const token = typeof window !== "undefined" ? localStorage.getItem("auth_token") : null
      if (!token) {
        setError("Please sign in to view your dashboard.")
        setAnalyses([])
        return
      }
      const res = await fetch("/api/Analysis/recent?count=100", {
        headers: { Authorization: `Bearer ${token}` },
      })
      if (res.status === 401) {
        setError("Your session has expired — please sign in again.")
        setAnalyses([])
        return
      }
      if (!res.ok) throw new Error(`Failed to load analyses: ${res.statusText}`)
      const data: RecentRecord[] = await res.json()
      const mapped: DashboardAnalysis[] = (Array.isArray(data) ? data : []).map((r) => ({
        id: r.id,
        content: r.url || r.content || r.title,
        type: r.contentType === "url" ? "url" : "text",
        result: { verdict: r.verdict, score: r.score },
        timestamp: r.date ? new Date(r.date).getTime() : Date.now(),
        isFavorite: r.isFavorite,
      }))
      setAnalyses(mapped)
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load dashboard data")
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    load()
  }, [refreshTrigger])

  const refresh = () => setRefreshTrigger((n) => n + 1)

  return (
    <main className="max-w-7xl mx-auto px-6 py-10 space-y-8">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold">TruthLens Dashboard</h1>
          <p className="text-gray-600 mt-1">Analytics and database records</p>
        </div>
        <div className="flex gap-2">
          <a href="/landing.html">
            <Button variant="outline" size="sm">← Back to app</Button>
          </a>
          <Button onClick={refresh} variant="outline" size="sm">
            <RefreshCw className="h-4 w-4 mr-2" />
            Refresh
          </Button>
        </div>
      </div>

      {error && (
        <div className="p-4 bg-red-50 border border-red-200 rounded-lg text-red-700">
          {error}
        </div>
      )}

      {loading ? (
        <div className="py-20 text-center text-gray-500">Loading dashboard…</div>
      ) : (
        <AnalyticsDashboard analyses={analyses} />
      )}

      <DatabaseViewer refreshTrigger={refreshTrigger} />
    </main>
  )
}

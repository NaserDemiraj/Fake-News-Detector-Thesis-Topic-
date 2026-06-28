"use client"

import { useEffect, useState } from "react"
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { AlertCircle, RefreshCw, Database, Download } from "lucide-react"
import { Button } from "@/components/ui/button"

interface DatabaseRecord {
  Id: string
  Title: string
  Url: string
  Verdict: string
  Score: number
  Date: string
  FormattedDate: string
  RelativeDate: string
}

interface DatabaseViewerProps {
  refreshTrigger?: number
}

export default function DatabaseViewer({ refreshTrigger = 0 }: DatabaseViewerProps) {
  const [records, setRecords] = useState<DatabaseRecord[]>([])
  const [totalRecords, setTotalRecords] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const fetchDatabaseRecords = async () => {
    try {
      setLoading(true)
      setError(null)
      const response = await fetch("/api/Analysis/database")

      if (!response.ok) {
        throw new Error(`Failed to fetch records: ${response.statusText}`)
      }

      const data = await response.json()
      setRecords(data.records || [])
      setTotalRecords(data.totalRecords || 0)
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load database records")
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    fetchDatabaseRecords()
  }, [refreshTrigger])

  const getVerdictColor = (verdict: string) => {
    if (!verdict) return "bg-gray-100 text-gray-800 dark:bg-gray-900 dark:text-gray-200"
    if (verdict.toLowerCase().includes("fake")) return "bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-200"
    if (verdict.toLowerCase().includes("true")) return "bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200"
    return "bg-yellow-100 text-yellow-800 dark:bg-yellow-900 dark:text-yellow-200"
  }

  const exportToCSV = () => {
    if (records.length === 0) return

    const headers = ["ID", "Title", "URL", "Verdict", "Score", "Date"]
    const rows = records.map(r => [
      r.Id,
      r.Title,
      r.Url || "N/A",
      r.Verdict,
      r.Score,
      r.FormattedDate
    ])

    const csv = [headers, ...rows].map(row => row.map(cell => `"${cell}"`).join(",")).join("\n")

    const element = document.createElement("a")
    element.setAttribute("href", "data:text/csv;charset=utf-8," + encodeURIComponent(csv))
    element.setAttribute("download", `database-export-${new Date().toISOString().split("T")[0]}.csv`)
    element.style.display = "none"
    document.body.appendChild(element)
    element.click()
    document.body.removeChild(element)
  }

  if (error) {
    return (
      <Card className="border-red-200 bg-red-50 dark:bg-red-950 dark:border-red-800">
        <CardContent className="pt-6">
          <div className="flex items-start gap-3">
            <AlertCircle className="h-5 w-5 text-red-600 mt-0.5 flex-shrink-0" />
            <div>
              <h3 className="font-bold text-red-600 dark:text-red-400">Error Loading Database</h3>
              <p className="text-red-700 dark:text-red-300 text-sm">{error}</p>
              <Button onClick={fetchDatabaseRecords} variant="outline" size="sm" className="mt-3">
                <RefreshCw className="h-4 w-4 mr-2" />
                Retry
              </Button>
            </div>
          </div>
        </CardContent>
      </Card>
    )
  }

  return (
    <div className="w-full space-y-6">
      <Card className="shadow-lg border-0">
        <CardHeader>
          <div className="flex items-center justify-between">
            <div>
              <CardTitle className="flex items-center gap-2">
                <Database className="h-5 w-5" />
                Database Records
              </CardTitle>
              <CardDescription>
                Viewing all {totalRecords} analysis records from Neon PostgreSQL
              </CardDescription>
            </div>
            <div className="flex gap-2">
              <Button onClick={fetchDatabaseRecords} variant="outline" size="sm">
                <RefreshCw className="h-4 w-4 mr-2" />
                Refresh
              </Button>
              {records.length > 0 && (
                <Button onClick={exportToCSV} variant="outline" size="sm">
                  <Download className="h-4 w-4 mr-2" />
                  Export CSV
                </Button>
              )}
            </div>
          </div>
        </CardHeader>
        <CardContent>
          {loading ? (
            <div className="flex items-center justify-center py-12">
              <div className="text-center">
                <div className="animate-spin mb-4">
                  <Database className="h-8 w-8 text-blue-600 mx-auto" />
                </div>
                <p className="text-gray-600 dark:text-gray-400">Loading database records...</p>
              </div>
            </div>
          ) : records.length === 0 ? (
            <div className="text-center py-12">
              <Database className="h-12 w-12 text-gray-400 mx-auto mb-3 opacity-50" />
              <p className="text-gray-600 dark:text-gray-400 font-medium">No records found</p>
              <p className="text-sm text-gray-500 dark:text-gray-500">Start analyzing content to populate the database</p>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-gray-200 dark:border-gray-700 bg-gray-50 dark:bg-gray-800">
                    <th className="text-left py-3 px-4 font-semibold">Title</th>
                    <th className="text-left py-3 px-4 font-semibold">URL</th>
                    <th className="text-left py-3 px-4 font-semibold">Verdict</th>
                    <th className="text-center py-3 px-4 font-semibold">Score</th>
                    <th className="text-left py-3 px-4 font-semibold">Date</th>
                  </tr>
                </thead>
                <tbody>
                  {records.map((record) => (
                    <tr
                      key={record.Id}
                      className="border-b border-gray-200 dark:border-gray-700 hover:bg-gray-50 dark:hover:bg-gray-800 transition"
                    >
                      <td className="py-3 px-4">
                        <div className="font-medium text-gray-900 dark:text-gray-100 truncate max-w-xs">
                          {record.Title}
                        </div>
                      </td>
                      <td className="py-3 px-4">
                        {record.Url ? (
                          <a
                            href={record.Url}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="text-blue-600 dark:text-blue-400 hover:underline text-xs truncate max-w-xs inline-block"
                            title={record.Url}
                          >
                            {new URL(record.Url).hostname}
                          </a>
                        ) : (
                          <span className="text-gray-500">N/A</span>
                        )}
                      </td>
                      <td className="py-3 px-4">
                        <Badge className={getVerdictColor(record.Verdict)}>
                          {record.Verdict.replace(/_/g, " ")}
                        </Badge>
                      </td>
                      <td className="py-3 px-4 text-center">
                        <span className="font-semibold text-gray-900 dark:text-gray-100">{Math.round(record.Score)}%</span>
                      </td>
                      <td className="py-3 px-4 text-xs text-gray-600 dark:text-gray-400">
                        <div title={record.FormattedDate}>{record.RelativeDate}</div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {records.length > 0 && (
            <div className="mt-6 p-4 bg-blue-50 dark:bg-blue-950 border border-blue-200 dark:border-blue-800 rounded-lg">
              <h4 className="font-semibold text-blue-900 dark:text-blue-100 mb-2">📊 Database Information</h4>
              <ul className="text-sm text-blue-800 dark:text-blue-200 space-y-1">
                <li>
                  <strong>Provider:</strong> Neon PostgreSQL (Serverless on AWS)
                </li>
                <li>
                  <strong>Host:</strong> ep-super-grass-apqi27xr-pooler.c-7.us-east-1.aws.neon.tech
                </li>
                <li>
                  <strong>Database:</strong> neondb
                </li>
                <li>
                  <strong>Table:</strong> "SavedAnalyses"
                </li>
                <li>
                  <strong>Total Records:</strong> {totalRecords}
                </li>
                <li>
                  <strong>Connection:</strong> Via Entity Framework Core with Npgsql driver
                </li>
              </ul>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  )
}

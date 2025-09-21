package main

import (
	"fmt"
	"io"
	"net/http"

	"github.com/gin-gonic/gin"
)

type RadioRequest struct {
	Key         *string  `json:"key"`
	Radio       string   `json:"radio"`
	Frequency   uint64   `json:"frequency"`
	Mode        string   `json:"mode"`
	FrequencyRx *uint64  `json:"frequencyRx"`
	ModeRx      *string  `json:"modeRx"`
	Power       *float32 `json:"power"`
}

func main() {
	r := gin.Default()

	r.POST("/decode", func(ctx *gin.Context) {
		res, err := io.ReadAll(ctx.Request.Body)
		if err != nil {
			fmt.Printf("Error while processing json: %s\n", err.Error())
			ctx.String(http.StatusBadRequest, "Invalid JSON: ")
			return
		}
		fmt.Println(string(res))
		ctx.String(http.StatusOK, "OK")
	})

	r.POST("/radio", func(c *gin.Context) {
		var request RadioRequest

		if err := c.ShouldBindJSON(&request); err != nil {
			fmt.Printf("Error while processing json: %s\n", err.Error())
			c.String(http.StatusBadRequest, "Invalid JSON: ")
			return
		}

		fmt.Printf("Radio name: %s\n", request.Radio)
		fmt.Printf("Radio tx freq: %d\n", request.Frequency)
		fmt.Printf("Radio tx mode: %s\n", request.Mode)

		if request.FrequencyRx != nil {
			fmt.Printf("Radio rx freq: %d\n", *request.FrequencyRx)
		} else {
			fmt.Println("Radio rx freq: null")
		}

		if request.ModeRx != nil {
			fmt.Printf("Radio rx mode: %s\n", *request.ModeRx)
		} else {
			fmt.Println("Radio rx mode: null")
		}

		if request.Power != nil {
			fmt.Printf("Radio tx power: %f\n", *request.Power)
		} else {
			fmt.Println("Radio tx power: null")
		}

		c.String(http.StatusOK, "OK")
	})

	r.Run(":8080")
}
